using System.Globalization;
using System.Text;

namespace HocLieu.Common;

/// <summary>Bỏ dấu tiếng Việt phía C# — cặp của hàm `f_unaccent` phía PostgreSQL (search §9).</summary>
public static class Text
{
    public static string StripDiacritics(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var nfd = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) is not (UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format))
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Chuẩn hóa chuỗi tìm kiếm: bỏ dấu + lower + gộp khoảng trắng.</summary>
    public static string NormalizeForSearch(string? input)
    {
        var plain = StripDiacritics(input ?? string.Empty).ToLowerInvariant();
        return string.Join(' ', plain.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
