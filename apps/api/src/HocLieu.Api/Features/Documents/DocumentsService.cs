using Ganss.Xss;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Files;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Documents;

/// <summary>Lỗi validation form → endpoint trả 422 ProblemDetails.</summary>
public sealed class DocumentValidationException(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Client gửi updatedAt lệch → 409 (spec §8.4).</summary>
public sealed class DocumentConcurrencyException : Exception { }

public sealed record FavoriteRow(string Kind, long ItemId, string Title, string Slug, string? SectionName, short? Grade, DateTimeOffset AddedAt);

/// <summary>
/// CRUD tài liệu (owner/Admin) + hiển thị công khai (VisibleTo §4.3–4.4).
/// Mọi kiểm tra quyền ở đây, FE chỉ ẩn/hiện nút (spec §0.6).
/// </summary>
public sealed class DocumentsService(
    AppDbContext db,
    AppSettingsService settings,
    IAuditLogger audit,
    HtmlSanitizer sanitizer,
    TimeProvider time)
{
    // ========== KHU GIÁO VIÊN ==========

    private sealed record Resolved(
        string Title, string? Summary, Section Section, short? GradeId, long? SubjectId, long? YearId, short? WeekNo,
        ContentScope Scope, PublishMode Mode, DateTimeOffset? From, DateTimeOffset? Until,
        List<long> FileIds, long? CoverFileId, bool AllowGuestDownload, long? TeamId, string? DescriptionHtml);

    private static void AddErr(Dictionary<string, string[]> errors, string key, string message)
    {
        if (!errors.ContainsKey(key))
            errors[key] = [message];
    }

    /// <summary>Validate chung cho tạo/sửa (spec §4.2, §4.4, §5.2).</summary>
    private async Task<(Dictionary<string, string[]> Errors, Resolved? Ok)> ValidateCoreAsync(
        ViewerContext viewer, CreateDocumentRequest req, bool requireGrade, CancellationToken ct)
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
        else if (section.ContentKind == SectionContentKind.Quiz)
            AddErr(errors, "sectionId", "Chuyên mục này chỉ dành cho bài tập.");

        short? grade = req.GradeId;
        if (requireGrade && grade is not > 0 and < 6)
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

        // Note: `not` có precedence cao hơn `and` → phải viết ngoặc (>= 1 and <= 37)
        if (section is { RequireWeek: true } && req.WeekNo is not (>= 1 and <= 37))
            AddErr(errors, "weekNo", "Tuần (1–37) là bắt buộc với chuyên mục này.");
        else if (req.WeekNo is < 1 or > 37)
            AddErr(errors, "weekNo", "Tuần phải từ 1 đến 37.");

        ContentScope scope = section?.DefaultScope ?? ContentScope.Public;
        if (!string.IsNullOrWhiteSpace(req.Scope)
            && !Enum.TryParse<ContentScope>(req.Scope, true, out scope))
            AddErr(errors, "scope", "Phạm vi không hợp lệ.");

        var mode = section?.DefaultPublishMode ?? PublishMode.Visible;
        if (!string.IsNullOrWhiteSpace(req.PublishMode))
        {
            if (!Enum.TryParse<PublishMode>(req.PublishMode, true, out mode))
                AddErr(errors, "publishMode", "Trạng thái hiển thị không hợp lệ.");
        }

        var from = req.PublishFrom;
        var until = req.PublishUntil;
        if (mode == PublishMode.Scheduled)
        {
            if (from is null && until is null)
                AddErr(errors, "publishFrom", "Hẹn giờ cần ít nhất một mốc Từ hoặc Đến.");
            else if (from is not null && until is not null && from >= until)
                AddErr(errors, "publishUntil", "Mốc Đến phải sau mốc Từ.");
        }
        else
        {
            // Visible/Hidden xóa lịch (spec §4.4)
            from = null;
            until = null;
        }

        var fileIds = (req.FileIds ?? []).Distinct().ToList();
        if (fileIds.Count > 10)
            AddErr(errors, "fileIds", "Tối đa 10 file.");
        if (fileIds.Count > 0)
        {
            var owned = await db.Files.AsNoTracking()
                .Where(f => fileIds.Contains(f.Id) && f.OwnerId == viewer.UserId)
                .Select(f => f.Id).ToListAsync(ct);
            if (owned.Count != fileIds.Count)
                AddErr(errors, "fileIds", "Có file không thuộc về bạn.");
        }

        if (req.CoverFileId is not null)
        {
            var coverOwned = await db.Files.AsNoTracking()
                .AnyAsync(f => f.Id == req.CoverFileId && f.OwnerId == viewer.UserId, ct);
            if (!coverOwned)
                AddErr(errors, "coverFileId", "Ảnh bìa không hợp lệ.");
        }

        long? teamId = req.TeamId;
        if (teamId is null && viewer.UserId is not null)
            teamId = await db.TeamMembers.AsNoTracking()
                .Where(t => t.UserId == viewer.UserId)
                .OrderBy(t => t.JoinedAt)
                .Select(t => (long?)t.TeamId).FirstOrDefaultAsync(ct);
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

        var allowGuestDownload = req.AllowGuestDownload
            ?? await settings.GetBoolAsync(SettingKeys.DownloadGuestDefault, false, ct);

        var descriptionHtml = string.IsNullOrWhiteSpace(req.DescriptionHtml)
            ? null
            : sanitizer.Sanitize(req.DescriptionHtml);

        if (errors.Count > 0)
            return (errors, null);

        return (errors, new Resolved(
            title, string.IsNullOrWhiteSpace(req.Summary) ? null : req.Summary.Trim(),
            section!, grade, req.SubjectId, yearId, req.WeekNo,
            scope, mode, from, until,
            fileIds, req.CoverFileId, allowGuestDownload, teamId, descriptionHtml));
    }

    public async Task<Document> CreateAsync(ViewerContext viewer, CreateDocumentRequest req, CancellationToken ct)
    {
        var (errors, r) = await ValidateCoreAsync(viewer, req, requireGrade: true, ct);
        if (r is null)
            throw new DocumentValidationException(errors);

        var requireReview = await settings.GetBoolAsync(SettingKeys.ContentRequireReview, false, ct);
        var moderation = requireReview && r.Scope == ContentScope.Public
            ? ModerationStatus.PendingReview
            : ModerationStatus.Approved;

        var now = time.GetUtcNow();
        var doc = new Document
        {
            Title = r.Title,
            Slug = Slugify.ToSlug(r.Title),
            Summary = r.Summary,
            DescriptionHtml = r.DescriptionHtml,
            SectionId = r.Section.Id,
            GradeId = r.GradeId,
            SubjectId = r.SubjectId,
            SchoolYearId = r.YearId,
            WeekNo = r.WeekNo,
            OwnerId = viewer.UserId,
            TeamId = r.TeamId,
            Scope = r.Scope,
            PublishMode = r.Mode,
            PublishFrom = r.From,
            PublishUntil = r.Until,
            ModerationStatus = moderation,
            AllowGuestDownload = r.AllowGuestDownload,
            CoverFileId = r.CoverFileId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < r.FileIds.Count; i++)
            db.DocumentFiles.Add(new DocumentFile { DocumentId = doc.Id, FileId = r.FileIds[i], Sort = (short)i });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("document.create", "Document", doc.Id.ToString(), new { doc.Title }, ct);
        return doc;
    }

    /// <summary>Trả null khi không tìm thấy hoặc không phải owner/Admin (endpoint 404).</summary>
    public async Task<Document?> UpdateAsync(ViewerContext viewer, long id, UpdateDocumentRequest req, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || (doc.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;

        if (!string.IsNullOrWhiteSpace(req.UpdatedAt)
            && DateTimeOffset.TryParse(req.UpdatedAt, out var clientUpdatedAt)
            && clientUpdatedAt != doc.UpdatedAt)
            throw new DocumentConcurrencyException();

        var createReq = new CreateDocumentRequest(
            req.Title, req.SectionId, req.GradeId, req.SubjectId, req.SchoolYearId, req.WeekNo,
            req.Scope, req.TeamId, req.Summary, req.DescriptionHtml,
            req.FileIds, req.CoverFileId, req.AllowGuestDownload,
            req.PublishMode, req.PublishFrom, req.PublishUntil);
        var (errors, r) = await ValidateCoreAsync(viewer, createReq, requireGrade: true, ct);
        if (r is null)
            throw new DocumentValidationException(errors);

        var requireReview = await settings.GetBoolAsync(SettingKeys.ContentRequireReview, false, ct);
        // §4.5: nội dung Public mới/sửa → PendingReview khi bật kiểm duyệt (không reset khi chỉ đổi ẩn/hiện)
        doc.ModerationStatus = requireReview && r.Scope == ContentScope.Public
            ? ModerationStatus.PendingReview
            : ModerationStatus.Approved;
        doc.ModerationNote = null;
        ApplyResolved(doc, r);
        doc.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("document.update", "Document", doc.Id.ToString(), new { doc.Title }, ct);
        return doc;
    }

    private void ApplyResolved(Document doc, Resolved r)
    {
        doc.Title = r.Title;
        doc.Slug = Slugify.ToSlug(r.Title);
        doc.Summary = r.Summary;
        doc.DescriptionHtml = r.DescriptionHtml;
        doc.SectionId = r.Section.Id;
        doc.GradeId = r.GradeId;
        doc.SubjectId = r.SubjectId;
        doc.SchoolYearId = r.YearId;
        doc.WeekNo = r.WeekNo;
        doc.TeamId = r.TeamId;
        doc.Scope = r.Scope;
        doc.PublishMode = r.Mode;
        doc.PublishFrom = r.From;
        doc.PublishUntil = r.Until;
        doc.AllowGuestDownload = r.AllowGuestDownload;
        doc.CoverFileId = r.CoverFileId;
        // thay toàn bộ file của tài liệu
        var old = db.DocumentFiles.Where(df => df.DocumentId == doc.Id).ToList();
        db.DocumentFiles.RemoveRange(old);
        for (var i = 0; i < r.FileIds.Count; i++)
            db.DocumentFiles.Add(new DocumentFile { DocumentId = doc.Id, FileId = r.FileIds[i], Sort = (short)i });
    }

    public async Task<Document?> SoftDeleteAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || (doc.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;
        doc.IsDeleted = true;
        doc.DeletedAt = time.GetUtcNow();
        doc.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("document.delete", "Document", id.ToString(), null, ct);
        return doc;
    }

    public async Task<Document?> RestoreAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var doc = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || doc.IsDeleted)
            return null;
        if (doc.OwnerId != viewer.UserId && !viewer.IsAdmin)
            return null;
        doc.IsDeleted = false;
        doc.DeletedAt = null;
        doc.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("document.restore", "Document", id.ToString(), null, ct);
        return doc;
    }

    /// <summary>Nhân bản: cùng file, tiêu đề + " (bản sao)", chế độ hiển thị mặc định của chuyên mục.</summary>
    public async Task<Document?> DuplicateAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var src = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (src is null || (src.OwnerId != viewer.UserId && !viewer.IsAdmin))
            return null;
        var fileIds = await db.DocumentFiles.AsNoTracking()
            .Where(df => df.DocumentId == id).OrderBy(df => df.Sort)
            .Select(df => df.FileId).ToListAsync(ct);
        var now = time.GetUtcNow();
        var copy = new Document
        {
            Title = src.Title + " (bản sao)",
            Slug = Slugify.ToSlug(src.Title + " (bản sao)"),
            Summary = src.Summary,
            DescriptionHtml = src.DescriptionHtml,
            SectionId = src.SectionId,
            GradeId = src.GradeId,
            SubjectId = src.SubjectId,
            SchoolYearId = src.SchoolYearId,
            WeekNo = src.WeekNo,
            ClassId = null,
            OwnerId = viewer.UserId,
            TeamId = src.TeamId,
            Scope = src.Scope,
            PublishMode = src.Section?.DefaultPublishMode ?? PublishMode.Visible,
            PublishFrom = null,
            PublishUntil = null,
            ModerationStatus = ModerationStatus.Approved,
            AllowGuestDownload = src.AllowGuestDownload,
            CoverFileId = src.CoverFileId,
            IsFeatured = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Documents.Add(copy);
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < fileIds.Count; i++)
            db.DocumentFiles.Add(new DocumentFile { DocumentId = copy.Id, FileId = fileIds[i], Sort = (short)i });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("document.duplicate", "Document", copy.Id.ToString(), new { from = id }, ct);
        return copy;
    }

    /// <summary>
    /// Hiện/Ẩn/Hẹn giờ — owner ∨ Lead|Deputy của tổ nội dung ∨ Admin (ma trận §2.2).
    /// Trả (ok, code): ok=false → code "not_found"|"validation".
    /// </summary>
    public async Task<(bool Ok, string? Code)> PublishAsync(ViewerContext viewer, long id, PublishRequest req, CancellationToken ct)
        => await PublishOneAsync(viewer, "document", id, req, ct);

    public async Task<BulkPublishResult> PublishBulkAsync(ViewerContext viewer, BulkPublishRequest req, CancellationToken ct)
    {
        var applied = 0;
        var failed = new List<BulkPublishFailure>();
        if (req.Items is null || req.Items.Count == 0)
            return new BulkPublishResult(0, failed);
        if (req.Items.Count > 100)
            return new BulkPublishResult(0, [new BulkPublishFailure("bulk", 0, "validation")]);
        foreach (var item in req.Items)
        {
            var kind = (item.Kind ?? "document").ToLowerInvariant();
            if (kind is not ("document" or "quiz"))
            {
                failed.Add(new BulkPublishFailure(item.Kind ?? "", item.Id, "validation"));
                continue;
            }
            var (ok, code) = await PublishOneAsync(viewer, kind, item.Id,
                new PublishRequest(req.Mode, req.From, req.Until), ct);
            if (ok)
                applied++;
            else
                failed.Add(new BulkPublishFailure(kind, item.Id, code ?? "not_found"));
        }
        return new BulkPublishResult(applied, failed);
    }

    private async Task<(bool Ok, string? Code)> PublishOneAsync(
        ViewerContext viewer, string kind, long id, PublishRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<PublishMode>(req.Mode, true, out var mode))
            return (false, "validation");
        var from = mode == PublishMode.Scheduled ? req.From : null;
        var until = mode == PublishMode.Scheduled ? req.Until : null;
        if (mode == PublishMode.Scheduled && from is null && until is null)
            return (false, "validation");
        if (from is not null && until is not null && from >= until)
            return (false, "validation");

        if (kind == "quiz")
        {
            var q = await db.Quizzes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (q is null || !viewer.CanManage(q.OwnerId, q.TeamId))
                return (false, "not_found");
            q.PublishMode = mode;
            q.PublishFrom = from;
            q.PublishUntil = until;
            q.UpdatedAt = time.GetUtcNow();
        }
        else
        {
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (doc is null || !viewer.CanManage(doc.OwnerId, doc.TeamId))
                return (false, "not_found");
            doc.PublishMode = mode;
            doc.PublishFrom = from;
            doc.PublishUntil = until;
            doc.UpdatedAt = time.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync($"content.publish:{mode}", kind, id.ToString(),
            new { from = from?.ToString("o"), until = until?.ToString("o") }, ct);
        return (true, null);
    }

    // ========== YÊU THÍCH (spec §5.2 /gv/yeu-thich) ==========

    public async Task<IReadOnlyList<FavoriteRow>> ListFavoritesAsync(long userId, CancellationToken ct)
    {
        var favs = await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);
        var docIds = favs.Where(f => f.ItemType == FavoriteItemType.Document).Select(f => f.ItemId).ToList();
        var quizIds = favs.Where(f => f.ItemType == FavoriteItemType.Quiz).Select(f => f.ItemId).ToList();
        var docs = docIds.Count == 0 ? new() : await db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Title, d.Slug, Grade = d.GradeId, Sec = d.Section == null ? (string?)null : d.Section.Name })
            .ToListAsync(ct);
        var quizzes = quizIds.Count == 0 ? new() : await db.Quizzes.AsNoTracking()
            .Where(q => quizIds.Contains(q.Id))
            .Select(q => new { q.Id, q.Title, q.Slug, Grade = q.GradeId, Sec = q.Section == null ? (string?)null : q.Section.Name })
            .ToListAsync(ct);
        var docMap = docs.ToDictionary(x => x.Id);
        var quizMap = quizzes.ToDictionary(x => x.Id);
        var result = new List<FavoriteRow>();
        foreach (var f in favs)
        {
            if (f.ItemType == FavoriteItemType.Document && docMap.TryGetValue(f.ItemId, out var d))
                result.Add(new FavoriteRow("document", d.Id, d.Title, d.Slug, d.Sec, d.Grade, f.CreatedAt));
            else if (f.ItemType == FavoriteItemType.Quiz && quizMap.TryGetValue(f.ItemId, out var q))
                result.Add(new FavoriteRow("quiz", q.Id, q.Title, q.Slug, q.Sec, q.Grade, f.CreatedAt));
        }
        return result;
    }

    /// <summary>Chỉ lưu khi nội dung tồn tại & hiển thị được với người dùng (không lộ nội dung ẩn).</summary>
    public async Task<bool> AddFavoriteAsync(ViewerContext viewer, FavoriteItemType itemType, long itemId, CancellationToken ct)
    {
        if (viewer.UserId is null)
            return false;
        var now = time.GetUtcNow();
        if (itemType == FavoriteItemType.Document)
        {
            var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == itemId, ct);
            if (doc is null || !FilesService.DocumentVisibleTo(doc, viewer, now))
                return false;
        }
        else
        {
            var q = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (q is null || !DocumentVisibleToQuiz(q, viewer, now))
                return false;
        }
        var exists = await db.Favorites.AnyAsync(f => f.UserId == viewer.UserId && f.ItemType == itemType && f.ItemId == itemId, ct);
        if (!exists)
        {
            db.Favorites.Add(new Favorite
            {
                UserId = viewer.UserId.Value,
                ItemType = itemType,
                ItemId = itemId,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(long userId, FavoriteItemType itemType, long itemId, CancellationToken ct)
    {
        var removed = await db.Favorites
            .Where(f => f.UserId == userId && f.ItemType == itemType && f.ItemId == itemId)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    private static bool DocumentVisibleToQuiz(Quiz q, ViewerContext viewer, DateTimeOffset now)
        => (q.IsLive(now) && viewer.ScopeAllows(q.Scope, q.TeamId))
           || viewer.IsAdmin
           || q.OwnerId == viewer.UserId
           || (q.TeamId is not null && viewer.LeadOrDeputyTeamIds.Contains(q.TeamId.Value));

    // ========== KHU CÔNG KHAI ==========

    public sealed record PublicListFilter(
        string? Kind, string? SectionSlug, short? Grade, string? SubjectSlug,
        long? YearId, short? Week, string? Q, string? Sort, int Page, int PageSize);

    /// <summary>
    /// Danh sách công khai: VisibleTo ∩ (Public, Teachers) — Team/Private không lộ ở khu công khai.
    /// Tìm: search_text (f_unaccent) ILIKE theo query đã chuẩn hóa (decisions.md M3).
    /// Trả danh sách đầy đủ (đã sắp xếp) + total — endpoint phân trang bằng PagedResult.Of.
    /// </summary>
    public async Task<(IReadOnlyList<PublicItemRow> Items, int Total)> ListPublicAsync(
        ViewerContext viewer, PublicListFilter f, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var normQ = string.IsNullOrWhiteSpace(f.Q) ? null : Text.NormalizeForSearch(f.Q);
        var kind = string.IsNullOrWhiteSpace(f.Kind) ? "document" : f.Kind.ToLowerInvariant();

        if (kind == "document")
        {
            var docs = await QueryDocs(viewer, f, normQ, now)
                .ToListAsync(ct);
            return (docs.Select(ToPublicItemRow).ToList(), docs.Count);
        }

        var quizzes = await QueryQuizzes(viewer, f, normQ, now)
            .ToListAsync(ct);
        return (quizzes.Select(ToPublicQuizRow).ToList(), quizzes.Count);
    }

    private sealed record DocRow(
        long Id, string Slug, string Title, string? Summary,
        string? SectionSlug, string? SectionName, short? Grade, string? SubjectName,
        short? WeekNo, string? YearName, int ViewCount, DateTimeOffset CreatedAt,
        PublishMode Mode, DateTimeOffset? From, DateTimeOffset? Until);

    private sealed record QuizRow(
        long Id, string Slug, string Title, short? Grade, string? SubjectName, short? WeekNo,
        string? SectionSlug, string? SectionName, string? YearName,
        int QuestionCount, int? TimeLimitMinutes, int ViewCount, DateTimeOffset CreatedAt,
        PublishMode Mode, DateTimeOffset? From, DateTimeOffset? Until);

    private IQueryable<DocRow> QueryDocs(
        ViewerContext viewer, PublicListFilter f, string? normQ, DateTimeOffset now)
    {
        var authed = viewer.UserId is not null; // không dùng pattern 'is' trong expression tree
        var q = db.Documents.AsNoTracking()
            .VisibleTo(viewer, now)
            .Where(d => d.Scope == ContentScope.Public
                        || (d.Scope == ContentScope.Teachers && authed));
        if (!string.IsNullOrWhiteSpace(f.SectionSlug))
            q = q.Where(d => d.Section != null && d.Section.Slug == f.SectionSlug);
        if (f.Grade is > 0 and < 6)
            q = q.Where(d => d.GradeId == f.Grade);
        if (!string.IsNullOrWhiteSpace(f.SubjectSlug))
            q = q.Where(d => d.Subject != null && d.Subject.Slug == f.SubjectSlug);
        if (f.YearId is not null)
            q = q.Where(d => d.SchoolYearId == f.YearId);
        if (f.Week is not null)
            q = q.Where(d => d.WeekNo == f.Week);
        if (normQ is not null)
            q = q.Where(d => d.SearchText != null && EF.Functions.ILike(d.SearchText, $"%{normQ}%"));

        var ordered = f.Sort == "popular"
            ? q.OrderByDescending(d => d.ViewCount).ThenByDescending(d => d.CreatedAt)
            : q.OrderByDescending(d => d.CreatedAt);

        return ordered.Select(d => new DocRow(
            d.Id, d.Slug, d.Title, d.Summary,
            d.Section == null ? null : d.Section.Slug,
            d.Section == null ? null : d.Section.Name,
            d.GradeId,
            d.Subject == null ? null : d.Subject.Name,
            d.WeekNo,
            d.SchoolYear == null ? null : d.SchoolYear.Name,
            d.ViewCount, d.CreatedAt, d.PublishMode, d.PublishFrom, d.PublishUntil));
    }

    private IQueryable<QuizRow> QueryQuizzes(
        ViewerContext viewer, PublicListFilter f, string? normQ, DateTimeOffset now)
    {
        var authed = viewer.UserId is not null;
        var q = db.Quizzes.AsNoTracking()
            .VisibleTo(viewer, now)
            .Where(x => x.Scope == ContentScope.Public
                        || (x.Scope == ContentScope.Teachers && authed));
        if (!string.IsNullOrWhiteSpace(f.SectionSlug))
            q = q.Where(x => x.Section != null && x.Section.Slug == f.SectionSlug);
        if (f.Grade is > 0 and < 6)
            q = q.Where(x => x.GradeId == f.Grade);
        if (!string.IsNullOrWhiteSpace(f.SubjectSlug))
            q = q.Where(x => x.Subject != null && x.Subject.Slug == f.SubjectSlug);
        if (f.YearId is not null)
            q = q.Where(x => x.SchoolYearId == f.YearId);
        if (f.Week is not null)
            q = q.Where(x => x.WeekNo == f.Week);
        if (normQ is not null)
            q = q.Where(x => x.SearchText != null && EF.Functions.ILike(x.SearchText, $"%{normQ}%"));

        // Quiz không có view_count — "Xem nhiều" theo lượt làm (decisions.md M3)
        var ordered = f.Sort == "popular"
            ? q.OrderByDescending(x => x.AttemptCount).ThenByDescending(x => x.CreatedAt)
            : q.OrderByDescending(x => x.CreatedAt);

        return ordered.Select(x => new QuizRow(
            x.Id, x.Slug, x.Title, x.GradeId,
            x.Subject == null ? null : x.Subject.Name, x.WeekNo,
            x.Section == null ? null : x.Section.Slug,
            x.Section == null ? null : x.Section.Name,
            x.SchoolYear == null ? null : x.SchoolYear.Name,
            x.QuestionCount, x.TimeLimitMinutes, x.AttemptCount, x.CreatedAt,
            x.PublishMode, x.PublishFrom, x.PublishUntil));
    }

    private PublicItemRow ToPublicItemRow(DocRow r)
        => new("document", r.Id, r.Slug, r.Title, r.Summary,
            r.SectionSlug, r.SectionName, r.Grade, r.SubjectName, r.WeekNo, r.YearName,
            0, null,
            Visibility.GetPublishState(r.Mode, r.From, r.Until, time.GetUtcNow()).ToString(),
            r.Until, r.ViewCount, r.CreatedAt);

    private PublicItemRow ToPublicQuizRow(QuizRow r)
        => new("quiz", r.Id, r.Slug, r.Title, null,
            r.SectionSlug, r.SectionName, r.Grade, r.SubjectName, r.WeekNo, r.YearName,
            r.QuestionCount, r.TimeLimitMinutes,
            Visibility.GetPublishState(r.Mode, r.From, r.Until, time.GetUtcNow()).ToString(),
            r.Until, r.ViewCount, r.CreatedAt);

    /// <summary>Chi tiết công khai: 404 (null) nếu không hiển thị với viewer; tăng view_count.</summary>
    public async Task<PublicDocumentDetailDto?> GetPublicDetailAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var doc = await db.Documents.AsNoTracking()
            .Include(d => d.Section).Include(d => d.Grade).Include(d => d.Subject)
            .Include(d => d.SchoolYear).Include(d => d.Owner).Include(d => d.Team)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || !FilesService.DocumentVisibleTo(doc, viewer, now))
            return null;

        await db.Documents.Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ViewCount, d => d.ViewCount + 1), ct);

        var files = await db.DocumentFiles.AsNoTracking()
            .Include(df => df.File)
            .Where(df => df.DocumentId == id)
            .OrderBy(df => df.Sort)
            .Select(df => new PublicFileDto(
                df.FileId,
                df.File.OriginalName,
                df.File.Bytes,
                df.File.Ext,
                df.File.PreviewPages,
                df.File.ProcessingStatus == ProcessingStatus.Ready || df.File.ProcessingStatus == ProcessingStatus.NotApplicable))
            .ToListAsync(ct);

        AuthorDto? author = null;
        var showAuthor = await settings.GetBoolAsync(SettingKeys.ContentShowAuthorPublic, true, ct);
        if (showAuthor && doc.Owner is not null)
            author = new AuthorDto(doc.Owner.FullName, doc.Team?.Name);

        return new PublicDocumentDetailDto(
            doc.Id, doc.Slug, doc.Title, doc.Summary, doc.DescriptionHtml,
            doc.Section?.Slug, doc.Section?.Name,
            doc.GradeId, doc.Subject?.Name, doc.WeekNo, doc.SchoolYear?.Name,
            author,
            files,
            doc.AllowGuestDownload,
            doc.ViewCount + 1,
            doc.CreatedAt.ToString("o"));
    }

    /// <summary>Tài liệu liên quan: cùng chuyên mục + khối, Public, live, 4 mục (spec §5.1).</summary>
    public async Task<IReadOnlyList<PublicItemRow>> GetRelatedAsync(ViewerContext viewer, long id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var current = await db.Documents.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.SectionId, d.GradeId })
            .FirstOrDefaultAsync(ct);
        if (current is null)
            return [];
        var rows = await db.Documents.AsNoTracking()
            .VisibleTo(viewer, now)
            .Where(d => d.Scope == ContentScope.Public && d.Id != id
                        && d.SectionId == current.SectionId
                        && d.GradeId == current.GradeId)
            .OrderByDescending(d => d.ViewCount)
            .Take(4)
            .Select(d => new DocRow(
                d.Id, d.Slug, d.Title, d.Summary,
                d.Section == null ? null : d.Section.Slug,
                d.Section == null ? null : d.Section.Name,
                d.GradeId,
                d.Subject == null ? null : d.Subject.Name,
                d.WeekNo,
                d.SchoolYear == null ? null : d.SchoolYear.Name,
                d.ViewCount, d.CreatedAt, d.PublishMode, d.PublishFrom, d.PublishUntil))
            .ToListAsync(ct);
        return rows.Select(ToPublicItemRow).ToList();
    }

    /// <summary>Báo lỗi nội dung (spec §5.1): chỉ với nội dung đang thấy được; IP lưu dạng hash.</summary>
    public async Task<(bool Ok, string Code)> SubmitReportAsync(
        ViewerContext viewer, ReportRequest req, string? ipHash, CancellationToken ct)
    {
        var itemType = (req.ItemType ?? string.Empty).ToLowerInvariant();
        if (itemType is not ("document" or "quiz") || req.ItemId is null
            || string.IsNullOrWhiteSpace(req.Reason))
            return (false, "validation");

        var now = time.GetUtcNow();
        if (itemType == "document")
        {
            var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.ItemId, ct);
            if (doc is null || !FilesService.DocumentVisibleTo(doc, viewer, now))
                return (false, "not_found");
        }
        else
        {
            var q = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.ItemId, ct);
            if (q is null || !DocumentVisibleToQuiz(q, viewer, now))
                return (false, "not_found");
        }

        var reason = req.Reason.Trim();
        db.ContentReports.Add(new ContentReport
        {
            ItemType = itemType == "document" ? FavoriteItemType.Document : FavoriteItemType.Quiz,
            ItemId = req.ItemId.Value,
            Reason = reason.Length > 300 ? reason[..300] : reason,
            Detail = string.IsNullOrWhiteSpace(req.Detail) ? null : req.Detail.Trim(),
            ReporterUserId = viewer.UserId,
            ReporterIpHash = ipHash,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return (true, "ok");
    }
}
