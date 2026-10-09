using System.Linq.Expressions;
using System.Text.Json;
using HocLieu.Common;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Attempts;

public sealed class AttemptNotFoundException(string title) : Exception(title);
public sealed class AttemptValidationException(Dictionary<string, string[]> errors) : Exception("Dữ liệu không hợp lệ")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
public sealed class AttemptConflictException(string title) : Exception(title);
public sealed class AttemptGoneException(string title) : Exception(title);
public sealed class AttemptLimitException : Exception;

/// <summary>Thứ tự câu & phương án sau khi trộn, lưu vào <c>attempts.layout</c> (spec §6.6).</summary>
public sealed record LayoutDto(IReadOnlyList<long> Q, IReadOnlyDictionary<long, IReadOnlyList<long>>? O);

/// <summary>
/// §6.6–6.7: vòng đời lượt làm bài của khách (tạo, lưu tự động, nộp, hết giờ).
/// Chấm điểm chỉ ở server (QuizScoring); DTO không chứa đáp án trước khi show_answers cho phép.
/// </summary>
public class AttemptsService(AppDbContext db, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    // ===== Tạo attempt =====

    /// <summary>Tạo attempt mới. Trả về null khi quiz không thấy được / không còn mở / không có câu.</summary>
    public async Task<AttemptDto?> CreateAsync(
        long quizId, string deviceId, CreateAttemptRequest? req, string? ipHash, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId, ct);
        // ClassOnly: học sinh chỉ vào được qua mã giao bài (CreateForAssignmentAsync).
        if (quiz is null || quiz.ClassOnly || !quiz.IsLive(now) || quiz.Scope != ContentScope.Public)
            return null;

        string? guestName = null;
        string? guestClass = null;
        if (quiz.IdentityMode is IdentityMode.Name or IdentityMode.NameAndClass)
        {
            guestName = req?.GuestName?.Trim();
            if (string.IsNullOrWhiteSpace(guestName))
                throw new AttemptValidationException(new() { ["guestName"] = ["Vui lòng nhập họ tên."] });
            if (guestName.Length > 100)
                throw new AttemptValidationException(new() { ["guestName"] = ["Họ tên tối đa 100 ký tự."] });
            guestClass = req?.GuestClass?.Trim();
            if (guestClass is { Length: > 60 })
                throw new AttemptValidationException(new() { ["guestClass"] = ["Lớp tối đa 60 ký tự."] });
        }
        return await CreateAttemptCoreAsync(quiz, assignmentId: null, studentId: null,
            guestName, guestClass, deviceId, ipHash, checkLive: false, ct);
    }

    /// <summary>
    /// §7: tạo attempt qua mã giao bài — không kiểm tra IsLive/Public của quiz
    /// (assignment đang mở là đủ); identity = studentId (danh sách lớp) hoặc thiết bị.
    /// Gọi sau khi AssignmentsService đã kiểm tra assignment + nhận diện.
    /// </summary>
    public async Task<AttemptDto?> CreateForAssignmentAsync(
        Quiz quiz, long assignmentId, long? studentId, string? guestName, string? guestClass,
        string deviceId, string? ipHash, CancellationToken ct)
    {
        return await CreateAttemptCoreAsync(quiz, assignmentId, studentId,
            guestName, guestClass, deviceId, ipHash, checkLive: false, ct);
    }

    private async Task<AttemptDto?> CreateAttemptCoreAsync(
        Quiz quiz, long? assignmentId, long? studentId, string? guestName, string? guestClass,
        string deviceId, string? ipHash, bool checkLive, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (checkLive && (!quiz.IsLive(now) || quiz.Scope != ContentScope.Public))
            return null;

        if (quiz.MaxAttempts is short max)
        {
            var used = await CountByIdentityAsync(quiz.Id, studentId, deviceId, ct);
            if (used >= max)
                throw new AttemptLimitException();
        }

        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quiz.Id)
            .OrderBy(q => q.Sort)
            .Include(q => q.Options)
            .ToListAsync(ct);
        if (questions.Count == 0)
            return null;

        var qIds = questions.Select(q => q.Id).ToList();
        var oMap = questions.ToDictionary(
            q => q.Id,
            q => q.Options.OrderBy(o => o.Sort).Select(o => o.Id).ToList());
        if (quiz.ShuffleQuestions)
            Shuffle(qIds);
        if (quiz.ShuffleOptions)
            foreach (var ids in oMap.Values)
                Shuffle(ids);

        var attempt = new Attempt
        {
            Quiz = quiz,
            QuizId = quiz.Id,
            AssignmentId = assignmentId,
            StudentId = studentId,
            DeviceId = deviceId,
            GuestName = guestName,
            GuestClass = guestClass,
            IpHash = ipHash,
            Layout = JsonSerializer.Serialize(
                new LayoutDto(qIds, oMap.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<long>)kv.Value)),
                JsonWeb),
            Status = AttemptStatus.InProgress,
            StartedAt = now,
            ExpiresAt = quiz.TimeLimitMinutes is { } mins ? now.AddMinutes(mins) : null,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        await db.Quizzes
            .Where(q => q.Id == quiz.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.AttemptCount, q => q.AttemptCount + 1), ct);

        return await BuildDtoAsync(attempt, ct);
    }

    // ===== Xem attempt (làm tiếp / xem kết quả) =====

    /// <summary>Idempotent: attempt InProgress đã quá hạn được chấm & chuyển Expired ngay tại đây.</summary>
    public async Task<AttemptDto?> GetAsync(Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .Include(a => a.Quiz)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Quiz is null)
            return null;

        var now = time.GetUtcNow();
        if (attempt.Status == AttemptStatus.InProgress
            && attempt.ExpiresAt is { } exp
            && now >= exp)
        {
            await ExpireAsync(attempt, ct);
            await db.SaveChangesAsync(ct);
        }
        return await BuildDtoAsync(attempt, ct);
    }

    // ===== Lưu câu trả lời (debounce từ FE) =====

    public async Task<IReadOnlyList<AttemptSavedAnswerDto>> SaveAnswersAsync(
        Guid attemptId, IReadOnlyList<SaveAnswerItem> items, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .Include(a => a.Quiz)
            .Include(a => a.Assignment)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Quiz is null)
            throw new AttemptNotFoundException("Không tìm thấy lượt làm bài.");

        var now = time.GetUtcNow();
        if (attempt.Status != AttemptStatus.InProgress)
            throw new AttemptConflictException("Lượt làm đã được nộp rồi.");
        if (attempt.ExpiresAt is { } exp && now > exp.AddSeconds(30))
            throw new AttemptGoneException("Hết thời gian làm bài.");
        if (!IsStillOpen(attempt, now))
            throw new AttemptConflictException("Bài tập đã đóng.");

        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == attempt.QuizId)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var qById = questions.ToDictionary(q => q.Id);
        var valid = items
            .Where(i => qById.ContainsKey(i.QuestionId) && i.OptionIds is { Count: > 0 })
            .ToList();
        if (valid.Count == 0)
            throw new AttemptValidationException(new() { ["answers"] = ["Câu trả lời không hợp lệ."] });

        var existing = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId && qById.Keys.Contains(a.QuestionId))
            .ToListAsync(ct);
        var existingByQ = existing.ToDictionary(a => a.QuestionId);

        var saved = new List<AttemptSavedAnswerDto>();
        foreach (var item in valid)
        {
            var optionIds = qById[item.QuestionId].Options.Select(o => o.Id).ToHashSet();
            var selected = item.OptionIds!
                .Where(id => optionIds.Contains(id))
                .Distinct()
                .ToArray();
            if (selected.Length == 0)
                continue;

            if (existingByQ.TryGetValue(item.QuestionId, out var ans))
            {
                ans.SelectedOptionIds = selected;
                ans.UpdatedAt = now;
            }
            else
            {
                ans = new AttemptAnswer
                {
                    AttemptId = attemptId,
                    QuestionId = item.QuestionId,
                    SelectedOptionIds = selected,
                    UpdatedAt = now,
                };
                db.AttemptAnswers.Add(ans);
                existingByQ[item.QuestionId] = ans;
            }
            saved.Add(new AttemptSavedAnswerDto(item.QuestionId, selected));
        }
        await db.SaveChangesAsync(ct);
        return saved;
    }

    // ===== Nộp bài =====

    /// <summary>Idempotent: attempt đã nộp/hết giờ → trả về kết quả hiện tại.</summary>
    public async Task<AttemptDto?> SubmitAsync(Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .Include(a => a.Quiz)
            .Include(a => a.Assignment)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Quiz is null)
            return null;

        var now = time.GetUtcNow();
        if (attempt.Status == AttemptStatus.InProgress)
        {
            if (attempt.ExpiresAt is { } exp && now > exp.AddSeconds(30))
                throw new AttemptGoneException("Hết thời gian làm bài.");
            if (!IsStillOpen(attempt, now))
                throw new AttemptConflictException("Bài tập đã đóng.");
            await AttemptFinalizer.RescoreAsync(db, attempt, time, ct);
            attempt.Status = AttemptStatus.Submitted;
            attempt.SubmittedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return await BuildDtoAsync(attempt, ct);
    }

    // ===== Dọn attempt bỏ dở (BackgroundService gọi mỗi 5 phút) =====

    /// <summary>Chấm & chuyển Expired: attempt quá hạn +30s, hoặc quiz không còn mở. Trả về số lượng.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var expiredIds = await db.Attempts
            .Where(a => a.Status == AttemptStatus.InProgress
                        && a.ExpiresAt != null
                        && a.ExpiresAt < now.AddSeconds(-30))
            .Select(a => a.Id)
            .ToListAsync(ct);

        var closedQuizIds = await db.Quizzes
            .Where(NotLive(now))
            .Select(q => q.Id)
            .ToListAsync(ct);
        var closedIds = await db.Attempts
            .Where(a => a.Status == AttemptStatus.InProgress && closedQuizIds.Contains(a.QuizId))
            .Select(a => a.Id)
            .ToListAsync(ct);

        // §6.6: assignment đã qua giờ đóng (+30s) → attempt InProgress còn lại chuyển Expired.
        var closedAssignmentIds = await db.Assignments
            .Where(a => a.CloseAt != null && a.CloseAt <= now.AddSeconds(-30))
            .Select(a => a.Id)
            .ToListAsync(ct);
        var closedByAssignmentIds = await db.Attempts
            .Where(a => a.Status == AttemptStatus.InProgress
                        && a.AssignmentId != null
                        && closedAssignmentIds.Contains(a.AssignmentId.Value))
            .Select(a => a.Id)
            .ToListAsync(ct);

        var ids = expiredIds.Concat(closedIds).Concat(closedByAssignmentIds).Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        var attempts = await db.Attempts
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(ct);
        var count = 0;
        foreach (var attempt in attempts)
        {
            if (attempt.Status != AttemptStatus.InProgress)
                continue;
            await ExpireAsync(attempt, ct);
            count++;
        }
        if (count > 0)
            await db.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>§6.6: quiz không còn mở (EF-translatable; dùng trong sweeper).</summary>
    public static Expression<Func<Quiz, bool>> NotLive(DateTimeOffset now)
        => q => q.ModerationStatus != ModerationStatus.Approved
            || (q.PublishMode != PublishMode.Visible
                && !(q.PublishMode == PublishMode.Scheduled
                     && (q.PublishFrom == null || q.PublishFrom <= now)
                     && (q.PublishUntil == null || now < q.PublishUntil)));

    // ===== Nội bộ =====

    private async Task ExpireAsync(Attempt attempt, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await AttemptFinalizer.RescoreAsync(db, attempt, time, ct);
        attempt.Status = AttemptStatus.Expired;
        attempt.SubmittedAt ??= attempt.ExpiresAt is { } exp && exp < now ? exp : now;
    }

    /// <summary>§6.6: giới hạn lượt theo studentId (chế độ danh sách lớp) hoặc theo thiết bị (cookie hl_dev).</summary>
    private async Task<int> CountByIdentityAsync(long quizId, long? studentId, string? deviceId, CancellationToken ct)
    {
        if (studentId is { } sid)
            return await db.Attempts.CountAsync(a => a.QuizId == quizId && a.StudentId == sid, ct);
        return await db.Attempts.CountAsync(a => a.QuizId == quizId && a.DeviceId == deviceId, ct);
    }

    /// <summary>
    /// §6.6: attempt qua mã giao bài → hiệu lực theo giờ của assignment (+30s grace cho
    /// lưu/nộp còn bay trong không trung); attempt thường → theo trạng thái mở của quiz.
    /// </summary>
    private static bool IsStillOpen(Attempt attempt, DateTimeOffset now)
    {
        if (attempt.AssignmentId is not null)
        {
            if (attempt.Assignment is not { } a)
                return false;
            if (a.OpenAt is { } open && now < open)
                return false;
            if (a.CloseAt is { } close && now >= close.AddSeconds(30))
                return false;
            return true;
        }
        return attempt.Quiz.IsLive(now);
    }

    private async Task<AttemptDto> BuildDtoAsync(Attempt attempt, CancellationToken ct)
    {
        var quiz = attempt.Quiz;
        var now = time.GetUtcNow();

        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == attempt.QuizId)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var groups = await db.QuestionGroups.AsNoTracking()
            .Where(g => g.QuizId == attempt.QuizId)
            .OrderBy(g => g.Sort)
            .ToListAsync(ct);
        var answers = await db.AttemptAnswers.AsNoTracking()
            .Where(a => a.AttemptId == attempt.Id)
            .ToListAsync(ct);
        var used = await CountByIdentityAsync(attempt.QuizId, attempt.StudentId, attempt.DeviceId, ct);

        LayoutDto? layout;
        try
        {
            layout = JsonSerializer.Deserialize<LayoutDto>(attempt.Layout, JsonWeb);
        }
        catch (JsonException)
        {
            layout = null;
        }

        var orderedQuestions = InLayoutOrder(layout, questions);
        var oMap = layout?.O;
        var questionDtos = orderedQuestions
            .Select((q, i) => new AttemptQuestionDto(
                q.Id, q.GroupId, i + 1, q.Type.ToString(), q.ContentHtml,
                OptionsInLayout(oMap, q).Select(o => new AttemptOptionDto(o.Id, o.ContentHtml)).ToList()))
            .ToList();
        var groupDtos = groups
            .Select(g => new AttemptGroupDto(g.Id, g.Title, g.PassageHtml))
            .ToList();
        var answerDtos = answers
            .Where(a => a.SelectedOptionIds is { Length: > 0 })
            .Select(a => new AttemptSavedAnswerDto(a.QuestionId, a.SelectedOptionIds))
            .ToList();

        var completed = attempt.Status != AttemptStatus.InProgress;
        var result = completed ? BuildResult(attempt, quiz, orderedQuestions, answers, CanReview(quiz, now)) : null;

        return new AttemptDto(
            attempt.Id.ToString(),
            attempt.Status.ToString(),
            attempt.ExpiresAt,
            quiz.Title,
            quiz.TimeLimitMinutes,
            questions.Count,
            quiz.IdentityMode.ToString(),
            quiz.MaxAttempts,
            used,
            attempt.GuestName ?? "",
            attempt.GuestClass ?? "",
            groupDtos,
            questionDtos,
            answerDtos,
            result);
    }

    /// <summary>§6.7: khi nào khách được xem lại đáp án.</summary>
    private static bool CanReview(Quiz quiz, DateTimeOffset now)
        => quiz.ShowAnswers switch
        {
            ShowAnswers.Never => false,
            ShowAnswers.AfterSubmit => true,
            ShowAnswers.AfterClose => !quiz.IsLive(now),
            _ => true,
        };

    private static AttemptResultDto BuildResult(
        Attempt attempt,
        Quiz quiz,
        List<Question> orderedQuestions,
        List<AttemptAnswer> answers,
        bool canReview)
    {
        var answerByQ = answers.ToDictionary(a => a.QuestionId);
        IReadOnlyList<AttemptReviewQuestionDto>? review = null;
        if (canReview)
        {
            review = orderedQuestions
                .Select((q, i) =>
                {
                    var selected = answerByQ.GetValueOrDefault(q.Id)?.SelectedOptionIds ?? [];
                    return new AttemptReviewQuestionDto(
                        q.Id,
                        i + 1,
                        q.ContentHtml,
                        q.Options.OrderBy(o => o.Sort)
                            .Select(o => new AttemptReviewOptionDto(o.Id, o.ContentHtml, selected.Contains(o.Id), o.IsCorrect))
                            .ToList(),
                        q.ExplanationHtml);
                })
                .ToList();
        }
        return new AttemptResultDto(
            attempt.Score10 ?? 0m,
            attempt.Score ?? 0m,
            quiz.TotalPoints,
            attempt.CorrectCount,
            attempt.QuestionCount,
            attempt.DurationSec,
            attempt.SubmittedAt ?? quiz.CreatedAt,
            canReview,
            review);
    }

    /// <summary>Câu theo thứ tự layout (trộn); fallback Sort khi layout thiếu/hỏng.</summary>
    private static List<Question> InLayoutOrder(LayoutDto? layout, List<Question> questions)
    {
        var byId = questions.ToDictionary(q => q.Id);
        var ordered = new List<Question>(questions.Count);
        if (layout?.Q is { Count: > 0 })
        {
            foreach (var id in layout.Q)
                if (byId.TryGetValue(id, out var q))
                    ordered.Add(q);
        }
        if (ordered.Count == 0)
            return questions.OrderBy(q => q.Sort).ToList();
        var have = ordered.Select(q => q.Id).ToHashSet();
        foreach (var q in questions.Where(q => !have.Contains(q.Id)).OrderBy(q => q.Sort))
            ordered.Add(q);
        return ordered;
    }

    /// <summary>Phương án theo thứ tự layout của câu; fallback Sort.</summary>
    private static List<QuestionOption> OptionsInLayout(
        IReadOnlyDictionary<long, IReadOnlyList<long>>? oMap, Question q)
    {
        var byId = q.Options.ToDictionary(o => o.Id);
        var layoutIds = oMap is null ? null : oMap.GetValueOrDefault(q.Id);
        if (layoutIds is { Count: > 0 })
        {
            var ordered = new List<QuestionOption>(layoutIds.Count);
            foreach (var id in layoutIds)
                if (byId.TryGetValue(id, out var o))
                    ordered.Add(o);
            if (ordered.Count > 0)
            {
                var have = ordered.Select(o => o.Id).ToHashSet();
                foreach (var o in q.Options.Where(o => !have.Contains(o.Id)).OrderBy(o => o.Sort))
                    ordered.Add(o);
                return ordered;
            }
        }
        return q.Options.OrderBy(o => o.Sort).ToList();
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
