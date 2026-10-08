using System.Text.RegularExpressions;

namespace HocLieu.Infrastructure.Conversion;

/// <summary>
/// Đếm trang PDF từ bytes — best effort (decisions.md M3):
/// quét cây trang chưa nén <c>/Type /Page</c>; fallback <c>/Count N</c> lớn nhất; fallback 1.
/// LibreOffice/Word xuất PDF không nén cây trang nên đếm đúng trong thực tế.
/// </summary>
public static class PdfPages
{
    private static readonly Regex PageType = new(@"(?m)/Type\s*/Page(?![sS])", RegexOptions.Compiled);
    private static readonly Regex CountEntry = new(@"(?m)/Count\s+(\d+)", RegexOptions.Compiled);
    private const int MaxPages = 1000;

    public static int Count(byte[] pdf)
    {
        if (pdf.Length == 0)
            return 1;

        // Latin-1 ánh xạ byte 1:1 → an toàn để quét regex trên bytes
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var count = PageType.Matches(text).Count;
        if (count == 0)
        {
            count = 0;
            foreach (Match m in CountEntry.Matches(text))
                if (int.TryParse(m.Groups[1].Value, out var c) && c > count)
                    count = c;
        }

        return count <= 0 ? 1 : Math.Min(count, MaxPages);
    }
}
