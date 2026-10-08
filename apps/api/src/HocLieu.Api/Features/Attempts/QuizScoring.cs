using HocLieu.Domain;
using HocLieu.Domain.Entities;

namespace HocLieu.Features.Attempts;

/// <summary>§6.7: chấm điểm chỉ ở server.</summary>
public static class QuizScoring
{
    /// <summary>
    /// Chấm toàn bộ câu của một lượt làm. Ghi <c>IsCorrect</c>/<c>PointsAwarded</c>
    /// vào từng câu trả lời; trả về (điểm tuyệt đối, số câu đúng).
    /// </summary>
    public static (decimal Score, int CorrectCount) ScoreAttempt(
        Quiz quiz,
        IEnumerable<Question> questions,
        IReadOnlyDictionary<long, AttemptAnswer> answers)
    {
        var score = 0m;
        var correctCount = 0;
        foreach (var q in questions)
        {
            var correctIds = q.Options.Where(o => o.IsCorrect).Select(o => (long)o.Id).ToHashSet();
            var answer = answers.GetValueOrDefault(q.Id);
            var selectedIds = answer is null
                ? new HashSet<long>()
                : (answer.SelectedOptionIds ?? []).ToHashSet();

            var ratio = Ratio(quiz, q, correctIds, selectedIds);
            var awarded = Math.Round(ratio * q.Points, 2, MidpointRounding.AwayFromZero);
            if (answer is not null)
            {
                answer.IsCorrect = ratio >= 1m;
                answer.PointsAwarded = awarded;
            }
            score += awarded;
            if (ratio >= 1m)
                correctCount++;
        }
        return (score, correctCount);
    }

    /// <summary>Tỷ lệ đúng của một câu (0..1).</summary>
    private static decimal Ratio(Quiz quiz, Question q, HashSet<long> correctIds, HashSet<long> selectedIds)
    {
        if (correctIds.Count == 0)
            return 0m;

        if (q.Type == QuestionType.Multi && quiz.MultiScoring == MultiScoring.Partial)
        {
            var rightSelected = selectedIds.Count(id => correctIds.Contains(id));
            var wrongSelected = selectedIds.Count(id => !correctIds.Contains(id));
            return Math.Max(0, rightSelected - wrongSelected) / (decimal)correctIds.Count;
        }

        // Single/TrueFalse, và Multi AllOrNothing (mặc định): đúng khi tập chọn = tập đúng
        return correctIds.SetEquals(selectedIds) ? 1m : 0m;
    }

    /// <summary>§6.7: <c>score_10 = round(score / total_points × 10)</c> theo <c>score_rounding</c>.</summary>
    public static decimal ToScore10(decimal score, decimal totalPoints, ScoreRounding rounding)
    {
        if (totalPoints <= 0m)
            return 0m;
        var raw = score / totalPoints * 10m;
        var step = rounding switch
        {
            ScoreRounding.None => 0.01m,
            ScoreRounding.Quarter => 0.25m,
            ScoreRounding.Half => 0.5m,
            ScoreRounding.Integer => 1m,
            _ => 0.25m,
        };
        return RoundToStep(raw, step);
    }

    /// <summary>Làm tròn tới gần nhất theo bước (trung gian làm tròn lên).</summary>
    public static decimal RoundToStep(decimal value, decimal step)
    {
        if (step <= 0m)
            return value;
        return Math.Floor(value / step + 0.5m) * step;
    }
}
