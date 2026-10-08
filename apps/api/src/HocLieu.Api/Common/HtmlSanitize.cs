using Ganss.Xss;

namespace HocLieu.Common;

/// <summary>§12: sanitize mọi HTML do người dùng nhập trước khi lưu (allowlist tag + thuộc tính).</summary>
public static class HtmlSanitize
{
    private static readonly string[] AllowedTagList =
    [
        "p", "br", "strong", "em", "u", "s", "sub", "sup",
        "ul", "ol", "li", "blockquote",
        "table", "thead", "tbody", "tr", "th", "td",
        "a", "img",
    ];

    private static readonly string[] AllowedAttributeList = ["href", "src", "alt"];

    public static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in AllowedTagList)
            s.AllowedTags.Add(tag);
        // HtmlSanitizer 9.x: AllowedAttributes là tập tên thuộc tính toàn cục (không theo từng tag)
        s.AllowedAttributes.Clear();
        foreach (var attr in AllowedAttributeList)
            s.AllowedAttributes.Add(attr);
        // Chỉ cho scheme https → bỏ rơi javascript:, vbscript:, data: (spec §12)
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https");
        return s;
    }

    public static string Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var cleaned = Sanitizer.Sanitize(html);
        // chỉ giữ link https (http → https)
        return cleaned
            .Replace("href=\"http://", "href=\"https://", StringComparison.Ordinal)
            .Replace("src=\"http://", "src=\"https://", StringComparison.Ordinal);
    }
}
