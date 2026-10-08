using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Ganss.Xss;
using HocLieu.Common;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Attempts;
using HocLieu.Features.Documents;
using HocLieu.Features.Files;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Storage;
using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Microsoft.AspNetCore.Http;
using QuestionType = HocLieu.Domain.QuestionType;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Quizzes;

/// <summary>Lỗi validation form quiz → endpoint trả 422 ProblemDetails.</summary>
public sealed class QuizValidationException(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Client gửi updatedAt lệch → 409 (spec §8.4).</summary>
public sealed class QuizConcurrencyException : Exception { }

/// <summary>
/// §6.1: đổi đáp án đúng / xóa câu / xóa phương án khi đã có lượt làm → 409 kèm số lượt
/// bị ảnh hưởng; FE xác nhận rồi gửi lại với <c>confirmRegrade=true</c>.
/// </summary>
public sealed class QuizRegradeConflictException(int affectedAttempts) : Exception
{
    public int AffectedAttempts { get; } = affectedAttempts;
}

/// <summary>
/// CRUD quiz (owner/Admin) + import Word/Excel (spec §6) + thống kê/xuất kết quả (§6.8).
/// Quiz luôn được tạo ở trạng thái Hidden (spec §4.4). Mọi kiểm tra quyền ở đây.
/// </summary>
public sealed class QuizzesService(
    AppDbContext db,
    AppSettingsService settings,
    IAuditLogger audit,
    HtmlSanitizer sanitizer,
    TimeProvider time,
    FilesService files,
    IFileStorage storage)
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    // ========== KHU GIÁO VIÊN ==========

    private sealed record Resolved(
        string Title, Section Section, short? GradeId, long? SubjectId, long? YearId, short? WeekNo,
        ContentScope Scope, long? TeamId, string? DescriptionHtml, long? PrintFileId,
        short? TimeLimitMinutes, bool ShuffleQuestions, bool ShuffleOptions,
        ShowAnswers ShowAnswers, IdentityMode IdentityMode, short? MaxAttempts,
        MultiScoring MultiScoring, ScoreRounding ScoreRounding);

    private static void AddErr(Dictionary<string, string[]> errors, string key, string message)
    {
        if (!errors.ContainsKey(key))
            errors[key] = [message];
    }

    /// <summary>Validate chung tạo/sửa (spec §4.2). Quiz: chuyên mục bắt buộc, luôn Hidden.</summary>
    private async Task<(Dictionary<string, string[]> Errors, Resolved? Ok)> ValidateCoreAsync(
        ViewerContext viewer, CreateQuizRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var title = (req.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            AddErr(errors, "title", "Tiêu đề là bắt buộc.");
        else if (title.Length > 300)
            AddErr(errors, "title", "Tiêu đề tối đa 300 ký tự.");

        var section = req.SectionId is null
            ? null
            : await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SectionId && s.IsActive, ct);
        if (section is null)
            AddErr(errors, "sectionId", "Chuyên mục không hợp lệ.");
        else if (section.ContentKind == SectionContentKind.Document)
            AddErr(errors, "sectionId", "Chuyên mục này chỉ dành cho tài liệu.");

        short? grade = req.GradeId;
        if (section?.IsInternal != true && grade is not > 0 and < 6)
            AddErr(errors, "gradeId", "Khối là bắt buộc.");
        else if (grade is not null)
        {
            var gradeExists = await db.Grades.AsNoTracking().AnyAsync(g => g.Id == grade, ct);
            if (!gradeExists)
                AddErr(errors, "gradeId", "Khối không hợp lệ.");
        }

        if (req.SubjectId is not null)
        {
            var subj = await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SubjectId, ct);
            if (subj is null)
                AddErr(errors, "subjectId", "Môn học không hợp lệ.");
        }

        long? yearId = req.SchoolYearId;
        if (yearId is null)
            yearId = await db.SchoolYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => y.Id).FirstOrDefaultAsync(ct);
        else
        {
            var yearExists = await db.SchoolYears.AsNoTracking().AnyAsync(y => y.Id == yearId, ct);
            if (!yearExists)
                AddErr(errors, "schoolYearId", "Năm học không hợp lệ.");
        }

        if (section is { RequireWeek: true } && req.WeekNo is not (>= 1 and <= 37))
            AddErr(errors, "weekNo", "Tuần (1–37) là bắt buộc với chuyên mục này.");
        else if (req.WeekNo is < 1 or > 37)
            AddErr(errors, "weekNo", "Tuần phải từ 1 đến 37.");

        // §4.1: Hồ sơ tổ là nội dung nội bộ → phạm vi cố định Tổ
        ContentScope scope = section?.DefaultScope ?? ContentScope.Public;
        if (!string.IsNullOrWhiteSpace(req.Scope))
        {
            if (!Enum.TryParse<ContentScope>(req.Scope, true, out scope))
                AddErr(errors, "scope", "Phạm vi không hợp lệ.");
            if (section?.IsInternal == true)
                scope = ContentScope.Team;
        }

        long? teamId = req.TeamId;
        if (teamId is null && viewer.UserId is not null)
            teamId = await db.TeamMembers.AsNoTracking()
                .Where(t => t.UserId == viewer.UserId)
                .OrderBy(t => t.JoinedAt)
                .Select(t => (long?)t.TeamId).FirstOrDefaultAsync(ct);
        if (section?.IsInternal == true && teamId is null)
            AddErr(errors, "teamId", "Hồ sơ tổ cần chọn tổ.");
        if (teamId is not null)
        {
            var ok = viewer.IsAdmin
                ? await db.Teams.AsNoTracking().AnyAsync(t => t.Id == teamId, ct)
                : await db.TeamMembers.AsNoTracking()
                    .AnyAsync(t => t.UserId == viewer.UserId && t.TeamId == teamId, ct);
            if (!ok)
            {
                AddErr(errors, "teamId", "Tổ không hợp lệ.");
                teamId = null;
            }
        }

        var s = req.Settings;
        short? timeLimit = s?.TimeLimitMinutes;
        if (timeLimit is not null && timeLimit is < 1 or > 600)
            AddErr(errors, "timeLimitMinutes", "Thời gian làm bài phải từ 1 đến 600 phút.");

        short? maxAttempts = s?.MaxAttempts;
        if (maxAttempts is not null && maxAttempts is < 1 or > 1000)
            AddErr(errors, "maxAttempts", "Số lượt tối đa phải từ 1 đến 1000.");

        var showAnswers = s?.ShowAnswers is { Length: > 0 } sa && Enum.TryParse<ShowAnswers>(sa, true, out var p1) ? p1 : ShowAnswers.AfterSubmit;
        var identity = s?.IdentityMode is { Length: > 0 } im && Enum.TryParse<IdentityMode>(im, true, out var p2) ? p2 : IdentityMode.Name;
        var multiScoring = s?.MultiScoring is { Length: > 0 } ms && Enum.TryParse<MultiScoring>(ms, true, out var p3) ? p3 : MultiScoring.AllOrNothing;
        var scoreRounding = s?.ScoreRounding is { Length: > 0 } sr && Enum.TryParse<ScoreRounding>(sr, true, out var p4) ? p4 : ScoreRounding.Quarter;

        if (req.PrintFileId is not null)
        {
            var owned = await db.Files.AsNoTracking()
                .AnyAsync(f => f.Id == req.PrintFileId && f.OwnerId == viewer.UserId, ct);
            if (!owned)
                AddErr(errors, "printFileId", "File đề in không hợp lệ.");
        }

        var descriptionHtml = string.IsNullOrWhiteSpace(req.DescriptionHtml)
            ? null
            : sanitizer.Sanitize(req.DescriptionHtml);

        if (errors.Count > 0)
            return (errors, null);

        return (errors, new Resolved(
            title, section!, grade, req.SubjectId, yearId, req.WeekNo,
            scope, teamId, descriptionHtml, req.PrintFileId,
            timeLimit, s?.ShuffleQuestions ?? false, s?.ShuffleOptions ?? false,
            showAnswers, identity, maxAttempts, multiScoring, scoreRounding));
    }

    public async Task<Quiz> CreateAsync(ViewerContext viewer, CreateQuizRequest req, CancellationToken ct)
    {
        var (errors, r) = await ValidateCoreAsync(viewer, req, ct);
        if (r is null)
            throw new QuizValidationException(errors);

        var requireReview = await settings.GetBoolAsync(SettingKeys.ContentRequireReview, false, ct);
        var moderation = requireReview && r.Scope == ContentScope.Public
            ? ModerationStatus.PendingReview
            : ModerationStatus.Approved;

        var now = time.GetUtcNow();
        var quiz = new Quiz
        {
            Title = r.Title,
            Slug = Slugify.ToSlug(r.Title),
            DescriptionHtml = r.DescriptionHtml,
            SectionId = r.Section.Id,
            GradeId = r.GradeId,
            SubjectId = r.SubjectId,
            SchoolYearId = r.YearId,
            WeekNo = r.WeekNo,
            OwnerId = viewer.UserId,
            TeamId = r.TeamId,
            Scope = r.Scope,
            // §4.4: bài tập luôn được tạo ở Hidden
            PublishMode = PublishMode.Hidden,
            PublishFrom = null,
            PublishUntil = null,
            ModerationStatus = moderation,
            TimeLimitMinutes = r.TimeLimitMinutes,
            ShuffleQuestions = r.ShuffleQuestions,
            ShuffleOptions = r.ShuffleOptions,
            ShowAnswers = r.ShowAnswers,
            IdentityMode = r.IdentityMode,
            MaxAttempts = r.MaxAttempts,
            MultiScoring = r.MultiScoring,
            ScoreRounding = r.ScoreRounding,
            PrintFileId = r.PrintFileId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("quiz.create", "Quiz", quiz.Id.ToString(), new { quiz.Title }, ct);
        return quiz;
    }

    /// <summary>
    /// PUT thay toàn bộ (spec §6.5): câu có id = cập nhật, không id = thêm, thiếu = xóa.
    /// Đổi đáp án đúng / xóa câu / xóa phương án khi đã có lượt làm →
    /// <see cref="QuizRegradeConflictException"/> (409) nếu chưa xác nhận chấm lại.
    /// </summary>
    public async Task<Quiz?> UpdateAsync(
        ViewerContext viewer, long id, UpdateQuizRequest req, bool confirmRegrade, CancellationToken ct)
    {
        var quiz = await db.Quizzes
            .Include(q => q.Groups)
            .Include(q => q.Questions).ThenInclude(qq => qq.Options)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || (quiz.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;

        if (!string.IsNullOrWhiteSpace(req.UpdatedAt)
            && DateTimeOffset.TryParse(req.UpdatedAt, out var clientUpdatedAt)
            && clientUpdatedAt != quiz.UpdatedAt)
            throw new QuizConcurrencyException();

        var createReq = new CreateQuizRequest(
            req.Title, req.SectionId, req.GradeId, req.SubjectId, req.SchoolYearId, req.WeekNo,
            req.Scope, req.TeamId, req.DescriptionHtml, req.PrintFileId, req.Settings);
        var (errors, r) = await ValidateCoreAsync(viewer, createReq, ct);
        if (r is null)
            throw new QuizValidationException(errors);

        // ---- §6.1: phát hiện thay đổi ảnh hưởng chấm điểm ----
        var conflict = false;
        if (quiz.AttemptCount > 0)
        {
            var existingQuestions = quiz.Questions.ToDictionary(q => q.Id);
            var incomingQuestionIds = (req.Questions ?? [])
                .Where(q => q.Id is not null).Select(q => q.Id!.Value).ToHashSet();

            // xóa câu
            if (existingQuestions.Keys.Any(qid => !incomingQuestionIds.Contains(qid)))
                conflict = true;

            foreach (var inQ in req.Questions ?? [])
            {
                if (inQ.Id is null || !existingQuestions.TryGetValue(inQ.Id.Value, out var exQ))
                    continue;
                var existingOptionIds = exQ.Options.Select(o => o.Id).ToHashSet();
                var incomingOptionIds = (inQ.Options ?? [])
                    .Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
                // xóa phương án
                if (existingOptionIds.Any(oid => !incomingOptionIds.Contains(oid)))
                {
                    conflict = true;
                    continue;
                }
                // đổi đáp án đúng
                foreach (var inO in inQ.Options ?? [])
                {
                    var exO = inO.Id is not null ? exQ.Options.FirstOrDefault(o => o.Id == inO.Id) : null;
                    if (exO is null)
                        continue;
                    if ((inO.IsCorrect ?? exO.IsCorrect) != exO.IsCorrect)
                    {
                        conflict = true;
                        break;
                    }
                }
            }

            if (conflict && !confirmRegrade)
            {
                var affected = await db.Attempts.CountAsync(a => a.QuizId == id
                    && a.Status != AttemptStatus.InProgress, ct);
                throw new QuizRegradeConflictException(affected);
            }
        }

        // ---- Áp dụng core ----
        var requireReview = await settings.GetBoolAsync(SettingKeys.ContentRequireReview, false, ct);
        quiz.ModerationStatus = requireReview && r.Scope == ContentScope.Public
            ? ModerationStatus.PendingReview
            : ModerationStatus.Approved;
        ApplyResolved(quiz, r);
        quiz.TimeLimitMinutes = r.TimeLimitMinutes;
        quiz.ShuffleQuestions = r.ShuffleQuestions;
        quiz.ShuffleOptions = r.ShuffleOptions;
        quiz.ShowAnswers = r.ShowAnswers;
        quiz.IdentityMode = r.IdentityMode;
        quiz.MaxAttempts = r.MaxAttempts;
        quiz.MultiScoring = r.MultiScoring;
        quiz.ScoreRounding = r.ScoreRounding;

        // ---- Nhóm: cập nhật / thêm / xóa ----
        var incomingGroups = (req.Groups ?? []).OrderBy(g => g.Sort ?? 0).ToList();
        var groupById = new Dictionary<long, QuestionGroup>();
        var newGroupCount = 0;
        foreach (var inG in incomingGroups)
        {
            var existing = inG.Id is not null ? quiz.Groups.FirstOrDefault(g => g.Id == inG.Id) : null;
            var g = existing ?? new QuestionGroup
            {
                QuizId = quiz.Id,
                Sort = inG.Sort ?? (short)newGroupCount,
            };
            if (existing is null)
            {
                newGroupCount++;
                db.QuestionGroups.Add(g);
                // Nhóm mới chưa có Id → FE tham chiếu bằng id tạm âm (-1..-N theo thứ tự gửi).
                groupById[-(long)newGroupCount] = g;
            }
            g.Sort = inG.Sort ?? g.Sort;
            g.Title = string.IsNullOrWhiteSpace(inG.Title) ? null : inG.Title.Trim();
            g.PassageHtml = string.IsNullOrWhiteSpace(inG.PassageHtml)
                ? null
                : sanitizer.Sanitize(inG.PassageHtml);
            if (existing is not null)
                groupById[g.Id] = g;
        }
        var keptGroupIds = groupById.Keys.ToHashSet();
        db.QuestionGroups.RemoveRange(quiz.Groups.Where(g => !keptGroupIds.Contains(g.Id)).ToList());

        // ---- Câu hỏi: cập nhật / thêm / xóa ----
        var incomingQuestions = (req.Questions ?? []).OrderBy(qq => qq.Sort ?? 0).ToList();
        var keptQuestionIds = new HashSet<long>();
        var totalPoints = 0m;
        var sort = 0;
        foreach (var inQ in incomingQuestions)
        {
            var existing = inQ.Id is not null ? quiz.Questions.FirstOrDefault(q => q.Id == inQ.Id) : null;
            var question = existing ?? new Question
            {
                QuizId = quiz.Id,
                Sort = (short)sort,
                Type = QuestionType.Single,
                ContentHtml = string.Empty,
                Points = 1m,
            };
            if (existing is null)
                db.Questions.Add(question);

            keptQuestionIds.Add(question.Id);
            question.Sort = inQ.Sort ?? (short)sort;
            sort++;
            if (inQ.Type is { Length: > 0 } t && Enum.TryParse<QuestionType>(t, true, out var qt))
                question.Type = qt;
            question.ContentHtml = sanitizer.Sanitize(inQ.ContentHtml ?? string.Empty);
            question.ExplanationHtml = string.IsNullOrWhiteSpace(inQ.ExplanationHtml)
                ? null
                : sanitizer.Sanitize(inQ.ExplanationHtml);
            question.Points = inQ.Points is > 0 and <= 100 ? inQ.Points.Value : 1m;
            question.GroupId = inQ.GroupId is not null && groupById.TryGetValue(inQ.GroupId.Value, out var grp)
                ? grp.Id
                : null;

            // Phương án: cập nhật / thêm / xóa
            var existingOptions = existing?.Options ?? [];
            var incomingOptions = (inQ.Options ?? []).OrderBy(o => o.Sort ?? 0).ToList();
            var keptOptionIds = new HashSet<long>();
            var optSort = 0;
            foreach (var inO in incomingOptions)
            {
                var exO = inO.Id is not null ? existingOptions.FirstOrDefault(o => o.Id == inO.Id) : null;
                var option = exO ?? new QuestionOption
                {
                    ContentHtml = string.Empty,
                };
                if (exO is null)
                    question.Options.Add(option); // FK đặt qua navigation (câu mới chưa có Id)
                keptOptionIds.Add(option.Id);
                option.Sort = inO.Sort ?? (short)optSort;
                optSort++;
                option.ContentHtml = sanitizer.Sanitize(inO.ContentHtml ?? string.Empty);
                option.IsCorrect = inO.IsCorrect ?? option.IsCorrect;
            }
            db.QuestionOptions.RemoveRange(
                (existing?.Options ?? []).Where(o => !keptOptionIds.Contains(o.Id)).ToList());
            totalPoints += question.Points;
        }
        db.Questions.RemoveRange(quiz.Questions.Where(q => !keptQuestionIds.Contains(q.Id)).ToList());

        quiz.QuestionCount = incomingQuestions.Count;
        quiz.TotalPoints = Math.Round(totalPoints, 2);
        quiz.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // ---- §6.1: xác nhận → chấm lại toàn bộ lượt đã nộp/hết hạn ----
        if (conflict && confirmRegrade)
        {
            var graded = await db.Attempts
                .Where(a => a.QuizId == quiz.Id && a.Status != AttemptStatus.InProgress)
                .ToListAsync(ct);
            foreach (var attempt in graded)
                await AttemptFinalizer.RescoreAsync(db, attempt, time, ct);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("quiz.regrade", "Quiz", quiz.Id.ToString(), new { graded = graded.Count }, ct);
        }

        await audit.LogAsync("quiz.update", "Quiz", quiz.Id.ToString(), new { quiz.Title }, ct);
        return quiz;
    }

    private void ApplyResolved(Quiz quiz, Resolved r)
    {
        quiz.Title = r.Title;
        quiz.Slug = Slugify.ToSlug(r.Title);
        quiz.DescriptionHtml = r.DescriptionHtml;
        quiz.SectionId = r.Section.Id;
        quiz.GradeId = r.GradeId;
        quiz.SubjectId = r.SubjectId;
        quiz.SchoolYearId = r.YearId;
        quiz.WeekNo = r.WeekNo;
        quiz.TeamId = r.TeamId;
        quiz.Scope = r.Scope;
        quiz.PrintFileId = r.PrintFileId;
    }

    // ========== IMPORT WORD/EXCEL (spec §6.2, §6.3, §6.4) ==========

    /// <summary>
    /// Parse file → tạo quiz Hidden ngay (không có bản nháp → không có ảnh mồ côi).
    /// Ảnh trong file: mỗi ảnh 1 hàng `files` gắn QuizId, src thay bằng /api/files/{id}/image.
    /// Lenient: thiếu tuần/khối không chặn import (GV bổ sung trong editor — decisions.md).
    /// </summary>
    public async Task<(long QuizId, IReadOnlyList<QuizWarningDto> Warnings)> ImportAsync(
        ViewerContext viewer, IFormFile file, long? sectionId, short? gradeId, short? weekNo, CancellationToken ct)
    {
        var ext = System.IO.Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (ext is not ("docx" or "xlsx"))
            throw new ImportException("Chỉ nhận file .docx hoặc .xlsx. Tải file mẫu về để xem định dạng.", 415);

        QuizDraft draft;
        using (var stream = file.OpenReadStream())
        {
            draft = ext == "docx"
                ? QuizImporter.ParseDocx(stream, file.Length)
                : QuizImporter.ParseXlsx(stream, file.Length);
        }
        if (draft.Questions.Count == 0)
            throw new ImportException("Không nhận diện được câu hỏi nào trong file.", 415);

        // §6.3.1: file gốc lưu làm source_file_id (GV có thể tải lại)
        var sourceFile = await files.UploadAsync(file, viewer.UserId!.Value, ct);

        var section = await ResolveImportSectionAsync(sectionId, ct);
        var scope = section.IsInternal ? ContentScope.Team : ContentScope.Public;
        long? teamId = null;
        if (section.IsInternal && viewer.UserId is not null)
            teamId = await db.TeamMembers.AsNoTracking()
                .Where(t => t.UserId == viewer.UserId)
                .OrderBy(t => t.JoinedAt)
                .Select(t => (long?)t.TeamId).FirstOrDefaultAsync(ct);

        var title = draft.Title.Trim();
        if (title.Length == 0)
            title = "Bài tập";
        if (title.Length > 300)
            title = title[..300];

        var requireReview = await settings.GetBoolAsync(SettingKeys.ContentRequireReview, false, ct);
        var now = time.GetUtcNow();
        var quiz = new Quiz
        {
            Title = title,
            Slug = Slugify.ToSlug(title),
            DescriptionHtml = null, // đặt sau khi upload ảnh (thay marker)
            SectionId = section.Id,
            GradeId = section.IsInternal ? null : gradeId,
            SchoolYearId = await db.SchoolYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => y.Id).FirstOrDefaultAsync(ct),
            WeekNo = weekNo,
            OwnerId = viewer.UserId,
            TeamId = teamId,
            Scope = scope,
            PublishMode = PublishMode.Hidden,
            ModerationStatus = requireReview && scope == ContentScope.Public ? ModerationStatus.PendingReview : ModerationStatus.Approved,
            SourceFileId = sourceFile.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);

        // ---- Ảnh: mỗi ảnh 1 hàng files + upload storage (spec §6.3.11) ----
        var imgFileIds = new Dictionary<string, long>(); // tempId -> file.Id
        for (var i = 0; i < draft.Images.Count; i++)
        {
            var img = draft.Images[i];
            var imgExt = ImageExtOf(img.ContentType);
            var imgFile = new FileEntity
            {
                OwnerId = viewer.UserId,
                OriginalName = $"anh-{i + 1}.{imgExt}",
                Ext = imgExt,
                Mime = string.IsNullOrWhiteSpace(img.ContentType) ? "image/png" : img.ContentType,
                Bytes = img.Bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(img.Bytes)).ToLowerInvariant(),
                StoragePublicId = $"files/{quiz.Id}/img-{i}",
                StorageResourceType = "image",
                ProcessingStatus = ProcessingStatus.NotApplicable,
                QuizId = quiz.Id,
                CreatedAt = now,
            };
            db.Files.Add(imgFile);
            await db.SaveChangesAsync(ct);
            using var content = new MemoryStream(img.Bytes, writable: false);
            await storage.UploadOriginalAsync(imgFile, content, ct);
            imgFileIds[img.TempId] = imgFile.Id;
        }

        // Thay marker data-temp-id="img-N" → src="/api/files/{id}/image" TRƯỚC sanitize
        // (HtmlSanitizer 9.x giữ URL tương đối, bỏ data: — decisions.md)
        Func<string?, string?> finalizeHtml = html =>
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;
            var replaced = html;
            foreach (var (tempId, fileId) in imgFileIds)
                replaced = replaced.Replace(
                    $"data-temp-id=\"{tempId}\"",
                    $"src=\"/api/files/{fileId}/image\"",
                    StringComparison.Ordinal);
            return sanitizer.Sanitize(replaced);
        };

        quiz.DescriptionHtml = finalizeHtml(draft.DescriptionHtml);

        var groupMap = new Dictionary<string, long>(); // groupTempId -> group.Id
        for (var i = 0; i < draft.Groups.Count; i++)
        {
            var g = draft.Groups[i];
            var group = new QuestionGroup
            {
                QuizId = quiz.Id,
                Sort = (short)i,
                Title = string.IsNullOrWhiteSpace(g.Title) ? null : g.Title,
                PassageHtml = finalizeHtml(g.PassageHtml),
            };
            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync(ct);
            groupMap[g.TempId] = group.Id;
        }

        var totalPoints = 0m;
        for (var i = 0; i < draft.Questions.Count; i++)
        {
            var dq = draft.Questions[i];
            var question = new Question
            {
                QuizId = quiz.Id,
                Sort = (short)i,
                Type = ParseQuestionType(dq.Type),
                ContentHtml = finalizeHtml(dq.ContentHtml) ?? string.Empty,
                ExplanationHtml = finalizeHtml(dq.ExplanationHtml),
                Points = dq.Points is > 0 and <= 100 ? dq.Points : 1m,
                GroupId = dq.GroupTempId is not null && groupMap.TryGetValue(dq.GroupTempId, out var gid) ? gid : null,
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync(ct);
            for (var j = 0; j < dq.Options.Count; j++)
            {
                var dopt = dq.Options[j];
                db.QuestionOptions.Add(new QuestionOption
                {
                    QuestionId = question.Id,
                    Sort = (short)j,
                    ContentHtml = finalizeHtml(dopt.ContentHtml) ?? string.Empty,
                    IsCorrect = dopt.IsCorrect,
                });
            }
            totalPoints += question.Points;
        }
        await db.SaveChangesAsync(ct);

        quiz.QuestionCount = draft.Questions.Count;
        quiz.TotalPoints = Math.Round(totalPoints, 2);
        quiz.ImportWarnings = JsonSerializer.Serialize(draft.Warnings, JsonWeb);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("quiz.import", "Quiz", quiz.Id.ToString(),
            new { quiz.Title, sourceFileId = sourceFile.Id, warnings = draft.Warnings.Count }, ct);

        var warnings = draft.Warnings
            .Select(w => new QuizWarningDto(w.Code, w.QuestionNumber, w.Message))
            .ToList();
        return (quiz.Id, warnings);
    }

    private async Task<Section> ResolveImportSectionAsync(long? sectionId, CancellationToken ct)
    {
        if (sectionId is not null)
        {
            var s = await db.Sections.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == sectionId && x.IsActive
                    && x.ContentKind != SectionContentKind.Document, ct);
            if (s is not null)
                return s;
        }
        var fallback = await db.Sections.AsNoTracking()
            .Where(x => x.IsActive && x.ContentKind != SectionContentKind.Document)
            .OrderBy(x => x.Sort)
            .FirstOrDefaultAsync(ct);
        if (fallback is null)
            throw new ImportException("Chưa có chuyên mục dành cho bài tập. Admin cần tạo chuyên mục trước.", 422);
        return fallback;
    }

    private static HocLieu.Domain.QuestionType ParseQuestionType(HocLieu.QuizImport.Model.QuestionType t)
        => t switch
        {
            HocLieu.QuizImport.Model.QuestionType.Multi => HocLieu.Domain.QuestionType.Multi,
            HocLieu.QuizImport.Model.QuestionType.TrueFalse => HocLieu.Domain.QuestionType.TrueFalse,
            _ => HocLieu.Domain.QuestionType.Single,
        };

    private static string ImageExtOf(string contentType)
    {
        var c = (contentType ?? string.Empty).ToLowerInvariant();
        return c.Contains("jpeg") ? "jpg"
            : c.Contains("png") ? "png"
            : c.Contains("webp") ? "webp"
            : "png";
    }

    // ========== XÓA / KHÔI PHỤC / NHÂN BẢN / HIỂN THỊ ==========

    public async Task<Quiz?> SoftDeleteAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || (quiz.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;
        quiz.IsDeleted = true;
        quiz.DeletedAt = time.GetUtcNow();
        quiz.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("quiz.delete", "Quiz", id.ToString(), null, ct);
        return quiz;
    }

    public async Task<Quiz?> RestoreAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var quiz = await db.Quizzes.IgnoreQueryFilters().FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || !quiz.IsDeleted)
            return null;
        if (quiz.OwnerId != viewer.UserId && !viewer.IsAdmin)
            return null;
        quiz.IsDeleted = false;
        quiz.DeletedAt = null;
        quiz.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("quiz.restore", "Quiz", id.ToString(), null, ct);
        return quiz;
    }

    /// <summary>Nhân bản (kể cả từ quiz Public của đồng nghiệp, spec §5.2): bản mới luôn Hidden.</summary>
    public async Task<Quiz?> DuplicateAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var src = await db.Quizzes
            .Include(q => q.Groups)
            .Include(q => q.Questions).ThenInclude(qq => qq.Options)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (src is null)
            return null;
        // Không phải owner/Admin → chỉ nhân bản quiz đang thấy (Public live)
        if (src.OwnerId != viewer.UserId && !viewer.IsAdmin && !src.IsLive(time.GetUtcNow()))
            return null;

        var now = time.GetUtcNow();
        var copy = new Quiz
        {
            Title = src.Title + " (bản sao)",
            Slug = Slugify.ToSlug(src.Title + " (bản sao)"),
            DescriptionHtml = src.DescriptionHtml,
            SectionId = src.SectionId,
            GradeId = src.GradeId,
            SubjectId = src.SubjectId,
            SchoolYearId = src.SchoolYearId,
            WeekNo = src.WeekNo,
            OwnerId = viewer.UserId,
            TeamId = src.TeamId,
            Scope = src.Scope,
            PublishMode = PublishMode.Hidden,
            PublishFrom = null,
            PublishUntil = null,
            ModerationStatus = ModerationStatus.Approved,
            TimeLimitMinutes = src.TimeLimitMinutes,
            ShuffleQuestions = src.ShuffleQuestions,
            ShuffleOptions = src.ShuffleOptions,
            ShowAnswers = src.ShowAnswers,
            IdentityMode = src.IdentityMode,
            MaxAttempts = src.MaxAttempts,
            MultiScoring = src.MultiScoring,
            ScoreRounding = src.ScoreRounding,
            PrintFileId = src.PrintFileId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Quizzes.Add(copy);
        await db.SaveChangesAsync(ct);

        var groupMap = new Dictionary<long, long>();
        foreach (var g in src.Groups.OrderBy(g => g.Sort))
        {
            var ng = new QuestionGroup
            {
                QuizId = copy.Id,
                Sort = g.Sort,
                Title = g.Title,
                PassageHtml = g.PassageHtml,
            };
            db.QuestionGroups.Add(ng);
            await db.SaveChangesAsync(ct);
            groupMap[g.Id] = ng.Id;
        }
        foreach (var q in src.Questions.OrderBy(q => q.Sort))
        {
            var nq = new Question
            {
                QuizId = copy.Id,
                Sort = q.Sort,
                Type = q.Type,
                ContentHtml = q.ContentHtml,
                ExplanationHtml = q.ExplanationHtml,
                Points = q.Points,
                GroupId = q.GroupId is not null && groupMap.TryGetValue(q.GroupId.Value, out var gid) ? gid : null,
            };
            db.Questions.Add(nq);
            await db.SaveChangesAsync(ct);
            foreach (var o in q.Options.OrderBy(o => o.Sort))
                db.QuestionOptions.Add(new QuestionOption
                {
                    QuestionId = nq.Id,
                    Sort = o.Sort,
                    ContentHtml = o.ContentHtml,
                    IsCorrect = o.IsCorrect,
                });
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("quiz.duplicate", "Quiz", copy.Id.ToString(), new { from = id }, ct);
        return copy;
    }

    /// <summary>Hiện/Ẩn/Hẹn giờ — owner ∨ Lead|Deputy của tổ ∨ Admin (ma trận §2.2).</summary>
    public async Task<(bool Ok, string? Code)> PublishAsync(ViewerContext viewer, long id, PublishRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<PublishMode>(req.Mode, true, out var mode))
            return (false, "validation");
        var from = mode == PublishMode.Scheduled ? req.From : null;
        var until = mode == PublishMode.Scheduled ? req.Until : null;
        if (mode == PublishMode.Scheduled && from is null && until is null)
            return (false, "validation");
        if (from is not null && until is not null && from >= until)
            return (false, "validation");

        var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || !viewer.CanManage(quiz.OwnerId, quiz.TeamId))
            return (false, "not_found");
        quiz.PublishMode = mode;
        quiz.PublishFrom = from;
        quiz.PublishUntil = until;
        quiz.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync($"content.publish:{mode}", "Quiz", id.ToString(),
            new { from = from?.ToString("o"), until = until?.ToString("o") }, ct);
        return (true, null);
    }

    // ========== CHI TIẾT ==========

    /// <summary>Chi tiết cho tabs của /gv/bai-tap/:id. Null khi không tìm thấy hoặc không phải owner/Admin.</summary>
    public async Task<QuizDetailDto?> GetDetailAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var quiz = await db.Quizzes
            .Include(q => q.Section).Include(q => q.Grade).Include(q => q.Subject)
            .Include(q => q.SchoolYear).Include(q => q.Team).Include(q => q.Owner)
            .Include(q => q.Groups)
            .Include(q => q.Questions).ThenInclude(qq => qq.Options)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || (quiz.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;

        var now = time.GetUtcNow();
        return new QuizDetailDto(
            quiz.Id, quiz.Title, quiz.Slug, quiz.DescriptionHtml,
            quiz.SectionId, quiz.Section?.Slug, quiz.Section?.Name,
            quiz.GradeId, quiz.Grade?.Name,
            quiz.SubjectId, quiz.Subject?.Name,
            quiz.SchoolYearId, quiz.SchoolYear?.Name,
            quiz.WeekNo,
            quiz.TeamId, quiz.Team?.Name,
            quiz.OwnerId, quiz.Owner?.FullName,
            quiz.Scope.ToString(), quiz.PublishMode.ToString(),
            quiz.PublishFrom, quiz.PublishUntil,
            Visibility.GetPublishState(quiz.PublishMode, quiz.PublishFrom, quiz.PublishUntil, now).ToString(),
            quiz.ModerationStatus.ToString(), null,
            quiz.TimeLimitMinutes, quiz.ShuffleQuestions, quiz.ShuffleOptions,
            quiz.ShowAnswers.ToString(), quiz.IdentityMode.ToString(), quiz.MaxAttempts,
            quiz.MultiScoring.ToString(), quiz.ScoreRounding.ToString(),
            quiz.PrintFileId,
            quiz.ImportWarnings,
            quiz.QuestionCount, quiz.TotalPoints, quiz.AttemptCount,
            quiz.Groups.OrderBy(g => g.Sort)
                .Select(g => new QuizGroupDto(g.Id, g.Sort, g.Title, g.PassageHtml)).ToList(),
            quiz.Questions.OrderBy(q => q.Sort)
                .Select(q => new QuizQuestionDto(
                    q.Id, q.Sort, q.GroupId, q.Type.ToString(),
                    q.ContentHtml, q.ExplanationHtml, q.Points,
                    q.Options.OrderBy(o => o.Sort)
                        .Select(o => new QuizOptionDto(o.Id, o.Sort, o.ContentHtml, o.IsCorrect)).ToList()))
                .ToList(),
            quiz.IsDeleted,
            quiz.CreatedAt, quiz.UpdatedAt.ToString("o"));
    }

    /// <summary>Giới thiệu quiz khu công khai (spec §5.1). Null = 404 (không lộ nội dung ẩn).</summary>
    public async Task<PublicQuizDetailDto?> GetPublicDetailAsync(
        ViewerContext viewer, long id, string? deviceId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(q => q.Section).Include(q => q.Grade).Include(q => q.Subject).Include(q => q.SchoolYear)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null || !Visibility.QuizIntroVisibleTo(quiz, viewer, now))
            return null;

        string? inProgressAttemptId = null;
        if (deviceId is not null)
        {
            var inProgressId = await db.Attempts.AsNoTracking()
                .Where(a => a.QuizId == id
                    && a.Status == AttemptStatus.InProgress
                    && a.DeviceId == deviceId
                    && (a.ExpiresAt == null || a.ExpiresAt > now))
                .Select(a => a.Id)
                .FirstOrDefaultAsync(ct);
            if (inProgressId != Guid.Empty)
                inProgressAttemptId = inProgressId.ToString();
        }

        return new PublicQuizDetailDto(
            quiz.Id, quiz.Slug, quiz.Title, quiz.DescriptionHtml,
            quiz.Section?.Slug, quiz.Section?.Name,
            quiz.GradeId, quiz.Subject?.Name, quiz.WeekNo, quiz.SchoolYear?.Name,
            quiz.QuestionCount, quiz.TimeLimitMinutes,
            Visibility.GetPublishState(quiz.PublishMode, quiz.PublishFrom, quiz.PublishUntil, now).ToString(),
            quiz.PublishFrom, quiz.PublishUntil,
            quiz.IdentityMode.ToString(), quiz.MaxAttempts, quiz.AttemptCount,
            quiz.PrintFileId,
            inProgressAttemptId,
            quiz.CreatedAt.ToString("o"));
    }

    // ========== KẾT QUẢ & THỐNG KÊ (spec §6.8) ==========

    private async Task<Quiz?> GetForTeacherAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
        return quiz is not null && (quiz.OwnerId == viewer.UserId || viewer.IsAdmin) ? quiz : null;
    }

    /// <summary>
    /// Bảng lượt làm. <c>which</c> = null: mọi lượt; "best"/"first"/"last": 1 lượt/danh tính.
    /// Danh tính: học sinh (studentId) hoặc thiết bị + tên khách.
    /// </summary>
    public async Task<(IReadOnlyList<AttemptRowDto> Items, int Total)> ListAttemptsAsync(
        ViewerContext viewer, long quizId, long? assignmentId, string? which,
        int page, int pageSize, CancellationToken ct)
    {
        var quiz = await GetForTeacherAsync(viewer, quizId, ct);
        if (quiz is null)
            return ([], 0);

        var attempts = await db.Attempts.AsNoTracking()
            .Include(a => a.Student).ThenInclude(s => s!.Class)
            .Where(a => a.QuizId == quizId
                && a.Status != AttemptStatus.InProgress
                && (assignmentId == null || a.AssignmentId == assignmentId))
            .OrderBy(a => a.StartedAt)
            .ToListAsync(ct);

        var groups = attempts
            .GroupBy(a => a.StudentId is not null
                ? $"s{a.StudentId}"
                : $"d|{a.DeviceId}|{a.GuestName}")
            .ToList();

        IEnumerable<(Attempt a, int No)> rows;
        if (which is null)
        {
            var numbered = new List<(Attempt a, int No)>();
            foreach (var g in groups)
            {
                var ordered = g.OrderBy(a => a.StartedAt).ToList();
                for (var i = 0; i < ordered.Count; i++)
                    numbered.Add((ordered[i], i + 1));
            }
            rows = numbered.OrderByDescending(x => x.a.StartedAt);
        }
        else
        {
            var picked = new List<(Attempt a, int No)>();
            foreach (var g in groups)
            {
                var ordered = g.OrderBy(a => a.StartedAt).ToList();
                var pick = which switch
                {
                    "best" => ordered.OrderByDescending(a => a.Score10 ?? -1).First(),
                    "first" => ordered.First(),
                    _ => ordered.Last(), // "last" + giá trị lạ
                };
                picked.Add((pick, ordered.IndexOf(pick) + 1));
            }
            rows = picked
                .OrderBy(x => x.a.StudentId)
                .ThenByDescending(x => x.a.Score10 ?? -1)
                .ThenByDescending(x => x.a.StartedAt);
        }

        var total = which is null ? attempts.Count : groups.Count;
        var items = rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToAttemptRow(x.a, x.No))
            .ToList();
        return (items, total);
    }

    private static AttemptRowDto ToAttemptRow(Attempt a, int no) => new(
        a.Id.ToString(),
        a.Student?.FullName,
        a.Student?.Class?.Name,
        a.GuestName,
        a.Status.ToString(),
        a.Score10,
        a.CorrectCount,
        a.QuestionCount,
        a.DurationSec,
        a.StartedAt,
        a.SubmittedAt,
        no);

    /// <summary>Xóa lượt làm (lượt rác, spec §6.8). Attempt cascade xóa câu trả lời.</summary>
    public async Task<bool> DeleteAttemptAsync(ViewerContext viewer, long quizId, Guid attemptId, CancellationToken ct)
    {
        var quiz = await GetForTeacherAsync(viewer, quizId, ct);
        if (quiz is null)
            return false;
        var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == attemptId && a.QuizId == quizId, ct);
        if (attempt is null)
            return false;
        db.Attempts.Remove(attempt);
        if (attempt.Status != AttemptStatus.InProgress)
        {
            quiz.AttemptCount = Math.Max(0, quiz.AttemptCount - 1);
            db.Quizzes.Update(quiz);
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("quiz.attempt.delete", "Quiz", quizId.ToString(),
            new { attemptId = attemptId.ToString() }, ct);
        return true;
    }

    public async Task<QuizStatsDto?> GetStatsAsync(ViewerContext viewer, long quizId, CancellationToken ct)
    {
        var quiz = await GetForTeacherAsync(viewer, quizId, ct);
        if (quiz is null)
            return null;

        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.QuizId == quizId && a.Status != AttemptStatus.InProgress)
            .ToListAsync(ct);
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var answers = attemptIds.Count == 0
            ? []
            : await db.AttemptAnswers.AsNoTracking()
                .Where(x => attemptIds.Contains(x.AttemptId))
                .ToListAsync(ct);

        var scores = attempts
            .Where(a => a.Score10 is not null)
            .Select(a => a.Score10!.Value)
            .OrderBy(x => x)
            .ToList();
        decimal? MedianOf(IReadOnlyList<decimal> s)
        {
            if (s.Count == 0)
                return null;
            return s.Count % 2 == 1
                ? s[s.Count / 2]
                : Math.Round((s[s.Count / 2 - 1] + s[s.Count / 2]) / 2m, 2);
        }

        // Bucket 0..10 theo điểm gần nhất (decisions.md: score 0.25-step gộp bucket tròn)
        var distribution = Enumerable.Range(0, 11)
            .Select(b => new ScoreBucket(b, scores.Count(s => (int)Math.Round(s) == b)))
            .ToList();

        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Sort)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var questionStats = questions.Select(q =>
        {
            var qAnswers = answers.Where(a => a.QuestionId == q.Id).ToList();
            var attempted = qAnswers.Count;
            var correct = qAnswers.Count(a => a.IsCorrect == true);
            var picks = q.Options.OrderBy(o => o.Sort)
                .Select(o => new OptionPickRow(
                    o.Id,
                    StripTags(o.ContentHtml),
                    qAnswers.Count(a => (a.SelectedOptionIds ?? []).Contains(o.Id)),
                    o.IsCorrect))
                .ToList();
            return new QuestionStatRow(
                q.Id,
                q.Sort + 1,
                StripTags(q.ContentHtml),
                attempted,
                correct,
                attempted == 0 ? 0m : Math.Round((decimal)correct / attempted * 100m, 1),
                picks);
        }).ToList();

        return new QuizStatsDto(
            attempts.Count,
            scores.Count == 0 ? null : Math.Round(scores.Average(), 2),
            MedianOf(scores),
            scores.Count == 0 ? null : (int)Math.Round(scores.First()),
            scores.Count == 0 ? null : (int)Math.Round(scores.Last()),
            distribution,
            questionStats);
    }

    /// <summary>Xuất Excel 2 sheet: Kết quả + Thống kê câu (ClosedXML, spec §6.8). Ghi audit.</summary>
    public async Task<(byte[] Bytes, string FileName)?> ExportAsync(ViewerContext viewer, long quizId, CancellationToken ct)
    {
        var quiz = await GetForTeacherAsync(viewer, quizId, ct);
        if (quiz is null)
            return null;

        var attempts = await db.Attempts.AsNoTracking()
            .Include(a => a.Student).ThenInclude(s => s!.Class)
            .Where(a => a.QuizId == quizId && a.Status != AttemptStatus.InProgress)
            .OrderByDescending(a => a.Score10 ?? -1)
            .ThenBy(a => a.StartedAt)
            .ToListAsync(ct);
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var answers = attemptIds.Count == 0
            ? []
            : await db.AttemptAnswers.AsNoTracking()
                .Where(x => attemptIds.Contains(x.AttemptId)).ToListAsync(ct);
        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Sort)
            .Include(q => q.Options)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var vnTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

        // Sheet 1: Kết quả
        var ws = wb.Worksheets.Add("Ket qua");
        string[] headers = ["STT", "Ho ten", "Lop", "Diem (10)", "So cau dung", "Tong so cau",
            "Thoi gian (s)", "Trang thai", "Nap luc"];
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        var row = 2;
        foreach (var a in attempts)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = a.Student?.FullName ?? a.GuestName ?? "-";
            ws.Cell(row, 3).Value = a.Student?.Class?.Name ?? a.GuestClass ?? string.Empty;
            if (a.Score10 is not null)
            {
                ws.Cell(row, 4).Value = (double)a.Score10.Value;
                ws.Cell(row, 4).Style.NumberFormat.Format = "0.0#";
            }
            ws.Cell(row, 5).Value = a.CorrectCount ?? 0;
            ws.Cell(row, 6).Value = a.QuestionCount ?? 0;
            ws.Cell(row, 7).Value = a.DurationSec ?? 0;
            ws.Cell(row, 8).Value = a.Status.ToString();
            if (a.SubmittedAt is not null)
                ws.Cell(row, 9).Value = TimeZoneInfo
                    .ConvertTimeFromUtc(a.SubmittedAt.Value.UtcDateTime, vnTz)
                    .ToString("dd/MM/yyyy HH:mm");
            row++;
        }

        // Sheet 2: Thong ke cau
        var ws2 = wb.Worksheets.Add("Thong ke cau");
        ws2.Cell(1, 1).Value = "So";
        ws2.Cell(1, 2).Value = "Cau hoi";
        ws2.Cell(1, 3).Value = "So luot lam";
        ws2.Cell(1, 4).Value = "So dung";
        ws2.Cell(1, 5).Value = "% dung";
        for (var c = 0; c < 8; c++)
            ws2.Cell(1, 6 + c).Value = (char)('A' + c);
        ws2.Range(1, 1, 1, 13).Style.Font.Bold = true;
        var row2 = 2;
        foreach (var q in questions)
        {
            var qAnswers = answers.Where(a => a.QuestionId == q.Id).ToList();
            var correct = qAnswers.Count(a => a.IsCorrect == true);
            ws2.Cell(row2, 1).Value = q.Sort + 1;
            ws2.Cell(row2, 2).Value = StripTags(q.ContentHtml);
            ws2.Cell(row2, 3).Value = qAnswers.Count;
            ws2.Cell(row2, 4).Value = correct;
            if (qAnswers.Count > 0)
            {
                ws2.Cell(row2, 5).Value = Math.Round((double)(decimal)correct / qAnswers.Count * 100.0, 1);
                ws2.Cell(row2, 5).Style.NumberFormat.Format = "0.0\"%\"";
            }
            var col = 6;
            foreach (var o in q.Options.OrderBy(o => o.Sort).Take(8))
            {
                ws2.Cell(row2, col).Value = qAnswers.Count(a => (a.SelectedOptionIds ?? []).Contains(o.Id));
                col++;
            }
            row2++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        await audit.LogAsync("quiz.export", "Quiz", quizId.ToString(), new { rows = attempts.Count }, ct);
        return (ms.ToArray(), $"ket-qua-{quiz.Slug}.xlsx");
    }

    /// <summary>Cắt HTML → văn bản thuần cho bảng/Excel (tối đa 80 ký tự).</summary>
    private static string StripTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var text = Regex.Replace(html, "<[^>]*>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 80 ? text[..80] + "…" : text;
    }
}
