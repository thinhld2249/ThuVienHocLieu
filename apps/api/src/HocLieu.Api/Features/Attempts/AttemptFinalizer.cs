using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Attempts;

/// <summary>
/// §6.7: chấm/ chấm lại một lượt làm. <paramref name="attempt"/> phải đang được
/// <paramref name="db"/> theo dõi; câu trả lời được load theo dõi để ghi
/// <c>IsCorrect</c>/<c>PointsAwarded</c> và lưu chung một lần SaveChanges.
/// </summary>
public static class AttemptFinalizer
{
    public static async Task RescoreAsync(
        AppDbContext db,
        Attempt attempt,
        TimeProvider time,
        CancellationToken ct)
    {
        var quiz = await db.Quizzes.AsNoTracking()
            .FirstAsync(q => q.Id == attempt.QuizId, ct);
        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == attempt.QuizId)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attempt.Id)
            .ToListAsync(ct);

        var (score, correctCount) = QuizScoring.ScoreAttempt(
            quiz, questions, answers.ToDictionary(a => a.QuestionId));

        var now = time.GetUtcNow();
        attempt.Score = score;
        attempt.Score10 = QuizScoring.ToScore10(score, quiz.TotalPoints, quiz.ScoreRounding);
        attempt.CorrectCount = correctCount;
        attempt.QuestionCount = questions.Count;
        if (attempt.DurationSec is null)
            attempt.DurationSec = (int)(now - attempt.StartedAt).TotalSeconds;
    }
}
