using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;

namespace HocLieu.QuizImport.Docx;

/// <summary>
/// OMML (công thức Word) → văn bản/HTML đơn giản (spec §6.3.10):
/// m:f → a/b · m:sSup → x&lt;sup&gt;2&lt;/sup&gt; · m:sSub → x&lt;sub&gt;1&lt;/sub&gt; · m:rad → √(x).
/// Cấu trúc khác (d, nary, func, mat…) → lấy text + đánh dấu Simplified (cảnh báo EQUATION_SIMPLIFIED).
/// </summary>
public static partial class OmmlLinearizer
{
    [GeneratedRegex("^http://schemas\\.openxmlformats\\.org/officeDocument/2006/math$")]
    private static partial Regex MathNs();

    public static bool IsMathElement(OpenXmlElement el) => MathNs().IsMatch(el.NamespaceUri);

    /// <summary>Linearize một m:oMath (hoặc nhóm con OMML) thành HTML fragment.</summary>
    public static (string Html, bool Simplified) Linearize(OpenXmlElement mathEl)
    {
        var sb = new System.Text.StringBuilder();
        var simplified = false;
        foreach (var child in mathEl.Elements())
        {
            var (html, sim) = LinearizeNode(child);
            sb.Append(html);
            simplified |= sim;
        }
        return (sb.ToString(), simplified);
    }

    private static (string Html, bool Simplified) LinearizeNode(OpenXmlElement el)
    {
        if (!IsMathElement(el))
            return (el.InnerText, false);

        switch (el.LocalName)
        {
            // m:r — run chữ trong công thức (m:t bên trong)
            case "r":
                {
                    var text = string.Concat(el.Elements().Where(e => e.LocalName == "t").Select(t => t.InnerText));
                    return (text, false);
                }
            // m:f — phân số: num/den
            case "f":
                {
                    var num = el.Elements().FirstOrDefault(e => e.LocalName == "num");
                    var den = el.Elements().FirstOrDefault(e => e.LocalName == "den");
                    return (LinearizeParts(num) + "/" + LinearizeParts(den), false);
                }
            // m:sSup — x^2
            case "sSup":
                {
                    var e = el.Elements().FirstOrDefault(x => x.LocalName == "e");
                    var sup = el.Elements().FirstOrDefault(x => x.LocalName == "sup");
                    return ($"{LinearizeParts(e)}<sup>{LinearizeParts(sup)}</sup>", false);
                }
            // m:sSub — x_1
            case "sSub":
                {
                    var e = el.Elements().FirstOrDefault(x => x.LocalName == "e");
                    var sub = el.Elements().FirstOrDefault(x => x.LocalName == "sub");
                    return ($"{LinearizeParts(e)}<sub>{LinearizeParts(sub)}</sub>", false);
                }
            // m:sSubSup — x_i^2
            case "sSubSup":
                {
                    var e = el.Elements().FirstOrDefault(x => x.LocalName == "e");
                    var sub = el.Elements().FirstOrDefault(x => x.LocalName == "sub");
                    var sup = el.Elements().FirstOrDefault(x => x.LocalName == "sup");
                    return ($"{LinearizeParts(e)}<sub>{LinearizeParts(sub)}</sub><sup>{LinearizeParts(sup)}</sup>", false);
                }
            // m:rad — căn: √(x)
            case "rad":
                {
                    var e = el.Elements().FirstOrDefault(x => x.LocalName == "e");
                    return ("√(" + LinearizeParts(e) + ")", false);
                }
            // Cấu trúc phức tạp khác: tuyến tính hóa con rồi cảnh báo
            default:
                {
                    var parts = string.Concat(el.Elements().Select(c =>
                    {
                        var (h, _) = LinearizeNode(c);
                        return h;
                    }));
                    return (parts, true);
                }
        }
    }

    /// <summary>Linearize một nhóm con (m:num, m:den, m:e, m:sup…) — có thể là run hoặc nhóm lồng nhau.</summary>
    private static string LinearizeParts(OpenXmlElement? group)
    {
        if (group is null)
            return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var child in group.Elements())
        {
            var (html, _) = LinearizeNode(child);
            sb.Append(html);
        }
        return sb.ToString();
    }
}
