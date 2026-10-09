using System.Text.RegularExpressions;
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
        "a", "img", "iframe",
    ];

    // iframe: src (nhúng video) + thuộc tính hiển thị (decisions.md 2026-10-09)
    private static readonly string[] AllowedAttributeList =
        ["href", "src", "alt", "width", "height", "frameborder", "allow", "allowfullscreen", "title"];

    // Chỉ nhúng video từ các host này — chặn iframe trỏ site khác (clickjacking/leak)
    private static readonly string[] AllowedIframeHosts =
    [
        "youtube.com", "www.youtube.com", "youtube-nocookie.com", "www.youtube-nocookie.com",
        "youtu.be", "vimeo.com", "player.vimeo.com", "drive.google.com",
    ];

    // cặp <iframe>…</iframe> hoặc thẻ đơn (self-closing)
    private static readonly Regex IframeBlockRegex = new(
        @"<iframe\b[^>]*>.*?</iframe>|<iframe\b[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex IframeSrcRegex = new(
        @"src=""(?<src>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        // nâng http → https TRƯỚC sanitize (sanitizer chỉ cho scheme https — làm sau thì src đã bị loại)
        var normalized = html
            .Replace("href=\"http://", "href=\"https://", StringComparison.Ordinal)
            .Replace("src=\"http://", "src=\"https://", StringComparison.Ordinal);
        return RemoveNonAllowedIframes(Sanitizer.Sanitize(normalized));
    }

    private static string RemoveNonAllowedIframes(string html)
        => IframeBlockRegex.Replace(html, m =>
        {
            var srcMatch = IframeSrcRegex.Match(m.Value);
            if (srcMatch.Success)
            {
                try
                {
                    var host = new Uri(srcMatch.Groups["src"].Value).Host.ToLowerInvariant();
                    if (AllowedIframeHosts.Contains(host))
                        return m.Value;
                }
                catch (UriFormatException)
                {
                    // src không phải URL hợp lệ → xóa
                }
            }
            return string.Empty;
        });
}
