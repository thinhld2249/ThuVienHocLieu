namespace HocLieu.QuizImport.Model;

/// <summary>
/// Kết quả parse file Word/Excel → quiz (spec §6.3, output JSON).
/// `contentHtml` dùng `<img data-temp-id="img-N">` cho ảnh chưa upload; tầng API thay src.
/// </summary>
public class QuizDraft
{
    public string Title { get; set; } = string.Empty;
    public string DescriptionHtml { get; set; } = string.Empty;
    public List<DraftGroup> Groups { get; set; } = [];
    public List<DraftQuestion> Questions { get; set; } = [];
    public List<ImportWarning> Warnings { get; set; } = [];
    public List<DraftImage> Images { get; set; } = [];
}

public class DraftImage
{
    public string TempId { get; set; } = string.Empty; // img-0, img-1, ...
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "image/png";
}

public class DraftGroup
{
    public string TempId { get; set; } = string.Empty; // g1, g2, ...
    public string? Title { get; set; }
    public string? PassageHtml { get; set; }
}

public class DraftQuestion
{
    public int? Number { get; set; }
    public string? GroupTempId { get; set; }
    public QuestionType Type { get; set; } = QuestionType.Single;
    public string ContentHtml { get; set; } = string.Empty;
    public List<DraftOption> Options { get; set; } = [];
    public string? ExplanationHtml { get; set; }
    public string? AnswerSource { get; set; } // Asterisk | AnswerLine | AnswerKey | Formatting
    public decimal Points { get; set; } = 1m;
}

public class DraftOption
{
    public string Label { get; set; } = string.Empty; // A..H, hoặc Đúng/Sai
    public string ContentHtml { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class ImportWarning
{
    public ImportWarning() { }

    public ImportWarning(string code, int? questionNumber, string message)
    {
        Code = code;
        QuestionNumber = questionNumber;
        Message = message;
    }

    /// <summary>Mã cảnh báo theo spec §6.3.12 (chuỗi, giữ nguyên).</summary>
    public string Code { get; set; } = string.Empty;
    public int? QuestionNumber { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum QuestionType
{
    Single,
    Multi,
    TrueFalse,
}

/// <summary>Mã cảnh báo import (spec §6.3.12) — giá trị chuỗi đúng như output JSON.</summary>
public static class WarningCodes
{
    public const string NoCorrectAnswer = "NO_CORRECT_ANSWER";
    public const string TooFewOptions = "TOO_FEW_OPTIONS";
    public const string EmptyOption = "EMPTY_OPTION";
    public const string DuplicateOptionLabel = "DUPLICATE_OPTION_LABEL";
    public const string QuestionNumberGap = "QUESTION_NUMBER_GAP";
    public const string DuplicateQuestionNumber = "DUPLICATE_QUESTION_NUMBER";
    public const string AnswerKeyUnmatched = "ANSWER_KEY_UNMATCHED";
    public const string ConflictingAnswerSources = "CONFLICTING_ANSWER_SOURCES";
    public const string UnsupportedImageFormat = "UNSUPPORTED_IMAGE_FORMAT";
    public const string EquationSimplified = "EQUATION_SIMPLIFIED";
}

/// <summary>Nguồn đáp án đúng, ưu tiên giảm dần (spec §6.3.8).</summary>
public static class AnswerSources
{
    public const string Asterisk = "Asterisk";
    public const string AnswerLine = "AnswerLine";
    public const string AnswerKey = "AnswerKey";
    public const string Formatting = "Formatting";
    public const string None = "None";
}
