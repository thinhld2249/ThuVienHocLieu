namespace HocLieu.QuizImport.Docx;

/// <summary>Một đoạn chữ chạy liên tục trong dòng, kèm định dạng (spec §6.3.2).</summary>
public sealed class Run
{
    public string Text { get; set; } = string.Empty;
    /// <summary>Text là HTML an toàn sẵn có (từ equation OMML linearize) — không escape, không bọc tag.</summary>
    public bool Raw { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Superscript { get; set; }
    public bool Subscript { get; set; }
    /// <summary>Màu chữ hex 6 ký tự, không có # (vd "FF0000"). null = màu mặc định.</summary>
    public string? ColorHex { get; set; }
    /// <summary>Highlight (yellow, green…) nếu có.</summary>
    public string? Highlight { get; set; }
}

/// <summary>Ảnh inline trong câu hỏi/phương án. Bytes giữ lại cho tầng API upload.</summary>
public sealed class InlineImage
{
    public string TempId { get; set; } = string.Empty; // img-0, img-1, ...
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "image/png";
    /// <summary>WMF/EMF (thường do MathType) — không hiển thị được, chèn khung cảnh báo.</summary>
    public bool UnsupportedFormat { get; set; }
}

/// <summary>
/// Dòng logic sau khi làm phẳng body (đoạn, ô bảng, phương án tách từ 1 dòng vật lý).
/// Text gộp các run; Images theo thứ tự xuất hiện.
/// </summary>
public sealed class Line
{
    public List<Run> Runs { get; } = [];
    public List<InlineImage> Images { get; } = [];
    public bool InTable { get; set; }
    public bool EquationSimplified { get; set; }
    /// <summary>Vị trí trong bảng (spec §6.3.2) — dùng cho bảng đáp án 2 hàng (Câu | 1 | 2 / Đáp án | C | B).</summary>
    public int? TableIndex { get; set; }
    public int? RowIndex { get; set; }
    public int? CellIndex { get; set; }

    /// <summary>Toàn bộ chữ (kể cả từ equation đã linearize) — dùng cho phân loại regex.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));

    /// <summary>
    /// Định dạng "đồng nhất" của nội dung (không kể nhãn chữ cái do FE/parser gán):
    /// dùng cho nguồn đáp án Formatting (spec §6.3.8) — phương án đúng nếu TOÀN BỘ nội dung
    /// gạch chân, hoặc chữ đỏ (R≥180, G≤90, B≤90), hoặc highlight.
    /// </summary>
    public (bool Underline, bool Red, bool Highlight, bool Bold) UniformFormatting()
    {
        var contentRuns = Runs
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .ToList();
        if (contentRuns.Count == 0)
            return (false, false, false, false);
        return (
            contentRuns.All(r => r.Underline),
            contentRuns.All(IsRed),
            contentRuns.All(r => r.Highlight is not null),
            contentRuns.All(r => r.Bold));
    }

    private static bool IsRed(Run run)
        => run.ColorHex is { } c
           && ParseColor(c, out var cr, out var cg, out var cb)
           && cr >= 180 && cg <= 90 && cb <= 90;

    public static bool ParseColor(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (hex.Length == 6 &&
            int.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
            int.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
            int.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            return true;
        return false;
    }
}
