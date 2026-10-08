using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HocLieu.Common;

/// <summary>Bỏ dấu, đ→d, giữ a-z0-9-, tối đa 80 ký tự (spec §8.4).</summary>
public static partial class Slugify
{
    public const int MaxLength = 80;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"[^\w-]")]
    private static partial Regex NonSlug();

    public static string ToSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "tai-lieu";

        var nfd = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                                 or UnicodeCategory.EnclosingMark or UnicodeCategory.Format))
                sb.Append(ch);
        }
        var plain = new string([.. sb.ToString().Normalize(NormalizationForm.FormC).ToCharArray()])
            .Replace("đ", "d", StringComparison.Ordinal)
            .Replace("Đ", "d", StringComparison.Ordinal);

        var noPunct = NonAlnum().Replace(plain, "-").ToLowerInvariant();
        var slug = NonSlug().Replace(noPunct, string.Empty).Trim('-');
        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].TrimEnd('-', '-');
        return slug.Length == 0 ? "tai-lieu" : slug;
    }
}
