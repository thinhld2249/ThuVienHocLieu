using System.Text.RegularExpressions;

namespace HocLieu.QuizImport.Docx;

public enum LineKind
{
    GroupHeader,      // PHẦN I. TRẮC NGHIỆM
    GroupIntro,       // Đọc đoạn văn sau và trả lời câu 5–6:
    AnswerKeyHeader,  // ĐÁP ÁN / BẢNG ĐÁP ÁN (mở khối đáp án cuối file)
    QuestionStart,    // Câu 1: ... / 1. ...
    Option,           // A. ... / *B. ... (1 dòng có thể chứa nhiều phương án)
    TrueFalseOption,  // Đúng / *Sai
    AnswerLine,       // Đáp án: A, C
    ExplanationStart, // Giải thích: ...
    Text,             // còn lại
}

/// <summary>
/// Một phương án đã tách khỏi dòng (Asterisk, Label A..H, Rest = nội dung).
/// Start = vị trí nội dung (sau nhãn) trong text đã trim — dùng để lấy định dạng đúng của phần nội dung.
/// </summary>
public sealed record OptionPart(bool Asterisk, string Label, string Rest, int Start = 0);

public sealed class ClassifiedLine
{
    public LineKind Kind { get; init; }
    public Line Line { get; init; } = null!;
    public int? QuestionNumber { get; init; }
    public string QuestionText { get; init; } = string.Empty;
    public List<OptionPart> Options { get; init; } = [];
    public string? AnswerText { get; init; }
    public string? ExplanationText { get; init; }
    public string? GroupTitle { get; init; }
    /// <summary>Số ký tự whitespace đầu dòng bị trim — cộng vào Start của OptionPart khi chiếu về text gốc.</summary>
    public int LeadingWhitespace { get; init; }

    public string Text => Line.Text;
}

/// <summary>
/// Phân loại dòng theo regex spec §6.3.5 (không phân biệt hoa thường, ngoài mẫu số thuần).
/// Thứ tự ưu tiên: AnswerLine &gt; AnswerKeyHeader &gt; GroupHeader &gt; QuestionStart
/// &gt; TrueFalseOption &gt; Option &gt; ExplanationStart &gt; GroupIntro &gt; Text.
/// </summary>
public static partial class LineClassifier
{
    [GeneratedRegex(@"^(PHẦN|Phần)\s+([IVXLC]+|\d+)\b\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GroupHeaderRx();

    [GeneratedRegex(@"^(Đọc|Quan\s*sát|Dựa\s*vào|Cho)\b.*\b(câu|trả\s+lời)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GroupIntroRx();

    [GeneratedRegex(@"^(Đáp\s*án|ĐA)(\s*đúng)?\s*[:\-]\s*([A-H](?:\s*[,;]?\s*(?:và)?\s*[A-H])*|Đúng|Sai)\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerLineRx();

    [GeneratedRegex(@"^(BẢNG\s+)?ĐÁP\s*ÁN\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerKeyHeaderRx();

    [GeneratedRegex(@"^(Câu|Bài)\s*(\d{1,3})\s*[:.)]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionStartRx();

    [GeneratedRegex(@"^(\d{1,3})\s*[.)]\s+(\S.*)$")]
    private static partial Regex NumberedQuestionRx();

    [GeneratedRegex(@"^(\*?)\s*([A-Ha-h])\s*[.)]\s*(.*)$", RegexOptions.Singleline)]
    private static partial Regex OptionRx();

    // Vị trí mở đầu một phương án: đầu dòng, hoặc sau tab / ≥2 dấu cách
    [GeneratedRegex(@"(?:(?<=^)|(?<=[\t]))|(?<= {2,})", RegexOptions.CultureInvariant)]
    private static partial Regex OptionAnchorRx();

    [GeneratedRegex(@"^(\*?)\s*([A-Ha-h])[.)]\s")]
    private static partial Regex OptionHeadRx();

    [GeneratedRegex(@"^(\*?)\s*(Đúng|Sai)\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrueFalseOptionRx();

    [GeneratedRegex(@"^(Giải\s*thích|Lời\s*giải|Hướng\s*dẫn(?:\s*giải)?)\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplanationStartRx();

    /// <summary>Khớp mẫu câu hỏi / phương án không — DocxReader dùng để quyết định tách &lt;w:br/&gt; thành dòng mới.</summary>
    public static bool LooksLikeQuestionOrOption(string text)
    {
        var t = text.TrimStart();
        return QuestionStartRx().IsMatch(t)
               || NumberedQuestionRx().IsMatch(t)
               || OptionRx().IsMatch(t)
               || TrueFalseOptionRx().IsMatch(t);
    }

    public static ClassifiedLine Classify(Line line)
    {
        var trimmed = line.Text.Trim();
        var leading = line.Text.Length - line.Text.TrimStart().Length;

        if (trimmed.Length == 0 && line.Images.Count == 0)
            return new ClassifiedLine { Kind = LineKind.Text, Line = line };

        // 1. AnswerLine trước AnswerKeyHeader: "Đáp án: B" là đáp án của câu, "ĐÁP ÁN" là tiêu đề khối.
        var ans = AnswerLineRx().Match(trimmed);
        if (ans.Success)
            return new ClassifiedLine { Kind = LineKind.AnswerLine, Line = line, AnswerText = ans.Groups[3].Value.Trim(), LeadingWhitespace = leading };

        if (AnswerKeyHeaderRx().IsMatch(trimmed))
            return new ClassifiedLine { Kind = LineKind.AnswerKeyHeader, Line = line, LeadingWhitespace = leading };

        var gh = GroupHeaderRx().Match(trimmed);
        if (gh.Success)
            return new ClassifiedLine { Kind = LineKind.GroupHeader, Line = line, GroupTitle = trimmed, LeadingWhitespace = leading };

        var qs = QuestionStartRx().Match(trimmed);
        if (qs.Success)
            return new ClassifiedLine
            {
                Kind = LineKind.QuestionStart,
                Line = line,
                QuestionNumber = int.Parse(qs.Groups[2].Value),
                QuestionText = qs.Groups[3].Value,
                LeadingWhitespace = leading
            };

        var nq = NumberedQuestionRx().Match(trimmed);
        if (nq.Success)
            return new ClassifiedLine
            {
                Kind = LineKind.QuestionStart,
                Line = line,
                QuestionNumber = int.Parse(nq.Groups[1].Value),
                QuestionText = nq.Groups[2].Value,
                LeadingWhitespace = leading
            };

        var tf = TrueFalseOptionRx().Match(trimmed);
        if (tf.Success)
            return new ClassifiedLine
            {
                Kind = LineKind.TrueFalseOption,
                Line = line,
                Options = [new OptionPart(tf.Groups[1].Value == "*", tf.Groups[2].Value.Trim(), string.Empty)],
                LeadingWhitespace = leading
            };

        var opt = OptionRx().Match(trimmed);
        if (opt.Success)
        {
            var parts = SplitOptions(trimmed);
            return new ClassifiedLine { Kind = LineKind.Option, Line = line, Options = parts, LeadingWhitespace = leading };
        }

        var ex = ExplanationStartRx().Match(trimmed);
        if (ex.Success)
            return new ClassifiedLine { Kind = LineKind.ExplanationStart, Line = line, ExplanationText = ex.Groups[2].Value, LeadingWhitespace = leading };

        if (GroupIntroRx().IsMatch(trimmed))
            return new ClassifiedLine { Kind = LineKind.GroupIntro, Line = line, LeadingWhitespace = leading };

        return new ClassifiedLine { Kind = LineKind.Text, Line = line, LeadingWhitespace = leading };
    }

    /// <summary>
    /// Tách dòng chứa nhiều phương án: nhãn chữ cái A..H đứng ở đầu dòng hoặc sau tab / ≥2 dấu cách.
    /// Ví dụ "A. 5,07\t*B. 5,7    C. 57    D. 0,57" → 4 phần.
    /// </summary>
    public static List<OptionPart> SplitOptions(string text)
    {
        var heads = new List<(int Start, bool Asterisk, string Label)>();
        // quét các vị trí bắt đầu tiềm tàng
        for (var i = 0; i < text.Length; i++)
        {
            bool atAnchor = i == 0
                || text[i - 1] == '\t'
                || (text[i - 1] == ' ' && i >= 2 && text[i - 2] == ' ');
            if (!atAnchor)
                continue;
            var j = i;
            bool asterisk = false;
            if (j < text.Length && text[j] == '*') { asterisk = true; j++; }
            if (j < text.Length && (char.IsLetter(text[j]) && char.ToUpperInvariant(text[j]) >= 'A' && char.ToUpperInvariant(text[j]) <= 'H'))
            {
                var label = char.ToUpperInvariant(text[j]);
                j++;
                if (j < text.Length && (text[j] == '.' || text[j] == ')')
                    && (j + 1 >= text.Length || text[j + 1] == ' ' || text[j + 1] == '\t'))
                {
                    heads.Add((i, asterisk, label.ToString()));
                    i = j; // bỏ qua phần vừa khớp, tiếp tục quét
                }
            }
        }

        var result = new List<OptionPart>();
        for (var k = 0; k < heads.Count; k++)
        {
            var (start, asterisk, label) = heads[k];
            var end = k + 1 < heads.Count ? heads[k + 1].Start : text.Length;
            var raw = text[start..end];
            var lead = raw.Length - raw.TrimStart().Length;
            var part = raw.Trim();
            var m = OptionRx().Match(part);
            var rest = m.Success ? m.Groups[3].Value.Trim() : part;
            var contentStart = m.Success ? start + lead + m.Groups[3].Index : start;
            result.Add(new OptionPart(asterisk, label, rest, contentStart));
        }
        return result.Count > 0 ? result : new List<OptionPart>();
    }
}
