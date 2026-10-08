using System.IO.Packaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HocLieu.QuizImport.Model;
using OpenXmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;

namespace HocLieu.QuizImport.Docx;

/// <summary>
/// Làm phẳng body docx theo thứ tự tài liệu thành List&lt;Line&gt; (spec §6.3.2):
/// đoạn văn (kể cả trong ô bảng, duyệt theo hàng → ô), run kèm định dạng, ảnh, equation.
/// &lt;w:br/&gt; tách dòng logic mới nếu phần sau khớp mẫu câu hỏi/phương án, ngược lại giữ làm xuống dòng.
/// Nhãn đánh số tự động (numbering.xml) được chèn vào đầu dòng đầu của đoạn.
/// Ảnh resolve qua System.IO.Packaging (OpenXml 3.x không còn API công khai để đọc relationships).
/// </summary>
public static class DocxReader
{
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public sealed record DocxContent(List<Line> Lines, List<DraftImage> Images);

    public static DocxContent Read(Stream stream)
    {
        var bytes = ReadAllBytes(stream);

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), false);
        var main = doc.MainDocumentPart
            ?? throw new ImportException("File không phải tài liệu Word hợp lệ.");
        var body = main.Document?.Body
            ?? throw new ImportException("File Word trống hoặc hỏng.");
        var resolver = new PartResolver(bytes);

        var numbering = new NumberingResolver();
        numbering.Load(main.NumberingDefinitionsPart);

        var images = new List<DraftImage>();
        var lines = new List<Line>();
        var tableCounter = 0;

        foreach (var el in body.ChildElements)
        {
            switch (el)
            {
                case Paragraph p:
                    ReadParagraph(p, numbering, resolver, images, lines);
                    break;
                case Table table:
                    ReadTable(table, numbering, resolver, images, lines, tableCounter++);
                    break;
                default:
                    break; // sectPr, customXml…
            }
        }
        return new DocxContent(lines, images);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream { CanSeek: true } ms)
        {
            var pos = ms.Position;
            var all = ms.ToArray();
            ms.Position = pos;
            return all;
        }
        using var target = new MemoryStream();
        stream.CopyTo(target);
        return target.ToArray();
    }

    /// <summary>
    /// Map rId → bytes media (qua word/_rels/document.xml.rels).
    /// Đọc trước toàn bộ media khi khởi tạo để đóng package ngay.
    /// </summary>
    private sealed class PartResolver
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _media = new(StringComparer.Ordinal);

        public PartResolver(byte[] bytes)
        {
            try
            {
                // FileMode.Open = update mode → stream phải ghi được; đọc xong đóng ngay
                using var pkg = Package.Open(new MemoryStream(bytes), FileMode.Open);
                var docPart = pkg.GetPart(new Uri("/word/document.xml", UriKind.Relative));
                if (docPart is null)
                    return;
                foreach (var rel in docPart.GetRelationships())
                {
                    if (rel.TargetMode == TargetMode.External)
                        continue;
                    // Target có thể tương đối ("media/image1.png" — Word thật)
                    // hoặc tuyệt đối ("word/media/image1.png" — OpenXml sinh ra)
                    var target = rel.TargetUri.OriginalString.Trim();
                    if (!target.StartsWith('/'))
                        target = "/" + target;
                    var part = pkg.GetPart(new Uri(target, UriKind.Relative));
                    if (part is null || !part.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var s = part.GetStream();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    _media[rel.Id] = (ms.ToArray(), part.ContentType);
                }
            }
            catch
            {
                // file hỏng nhẹ → ảnh sẽ bị bỏ qua, phần text vẫn dùng được
            }
        }

        public (byte[] Bytes, string ContentType)? TryGet(string relId)
            => relId is not null && _media.TryGetValue(relId, out var v) ? v : null;
    }

    private static void ReadTable(Table table, NumberingResolver numbering, PartResolver resolver, List<DraftImage> images, List<Line> lines, int tableIndex)
    {
        var rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            var cellIndex = 0;
            foreach (var cell in row.Elements<TableCell>())
            {
                foreach (var p in cell.Descendants<Paragraph>())
                    ReadParagraph(p, numbering, resolver, images, lines, inTable: true, tableIndex, rowIndex, cellIndex);
                cellIndex++;
            }
            rowIndex++;
        }
    }

    private sealed class Segment
    {
        public List<Run> Runs { get; } = [];
        public List<InlineImage> Images { get; } = [];
        public bool Simplified { get; set; }

        public string Text => string.Concat(Runs.Select(r => r.Text));
    }

    private static void ReadParagraph(Paragraph p, NumberingResolver numbering, PartResolver resolver, List<DraftImage> images, List<Line> lines, bool inTable = false, int? tableIndex = null, int? rowIndex = null, int? cellIndex = null)
    {
        var tablePos = (tableIndex, rowIndex, cellIndex);

        Line NewLine() => new()
        {
            InTable = inTable,
            TableIndex = tablePos.tableIndex,
            RowIndex = tablePos.rowIndex,
            CellIndex = tablePos.cellIndex,
        };

        var segments = new List<Segment> { new() };

        void AddText(string text, Run fmt)
        {
            if (text.Length == 0)
                return;
            var seg = segments[^1];
            if (seg.Runs.Count > 0 && SameFormat(seg.Runs[^1], fmt))
                seg.Runs[^1].Text += text;
            else
                seg.Runs.Add(new Run
                {
                    Text = text,
                    Bold = fmt.Bold,
                    Italic = fmt.Italic,
                    Underline = fmt.Underline,
                    Superscript = fmt.Superscript,
                    Subscript = fmt.Subscript,
                    ColorHex = fmt.ColorHex,
                    Highlight = fmt.Highlight
                });
        }

        void AddImage(InlineImage img)
        {
            segments[^1].Images.Add(img);
            images.Add(new DraftImage { TempId = img.TempId, Bytes = img.Bytes, ContentType = img.ContentType });
        }

        void BreakSegment()
        {
            segments.Add(new Segment());
        }

        foreach (var child in p.ChildElements)
        {
            switch (child)
            {
                case OpenXmlRun r:
                    ReadRun(r, resolver, images, AddText, AddImage, BreakSegment);
                    break;
                case Hyperlink h:
                    foreach (var r in h.Elements<OpenXmlRun>())
                        ReadRun(r, resolver, images, AddText, AddImage, BreakSegment);
                    break;
                default:
                    if (child.LocalName is "oMath" or "oMathPara" && OmmlLinearizer.IsMathElement(child))
                    {
                        var (html, simplified) = OmmlLinearizer.Linearize(child);
                        segments[^1].Runs.Add(new Run { Text = html, Raw = true });
                        segments[^1].Simplified |= simplified;
                    }
                    break;
            }
        }

        // Gộp segment thành dòng logic: tách dòng mới nếu phần sau khớp mẫu câu hỏi/phương án
        var firstLineIndex = lines.Count;
        var current = NewLine();
        current.EquationSimplified = segments[0].Simplified;
        MergeSegment(current, segments[0]);
        for (var i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (LineClassifier.LooksLikeQuestionOrOption(seg.Text))
            {
                lines.Add(current);
                current = NewLine();
                current.EquationSimplified = seg.Simplified;
                MergeSegment(current, seg);
            }
            else if (seg.Runs.Count > 0 || seg.Images.Count > 0)
            {
                // giữ làm xuống dòng trong cùng một dòng logic
                current.EquationSimplified |= seg.Simplified;
                if (current.Runs.Count > 0 && seg.Runs.Count > 0)
                    current.Runs.Add(new Run { Text = "\n" });
                foreach (var run in seg.Runs)
                    current.Runs.Add(run);
                foreach (var img in seg.Images)
                    current.Images.Add(img);
            }
        }
        lines.Add(current);

        // Nhãn đánh số tự động — chỉ gán cho dòng đầu của đoạn
        var numPr = p.ParagraphProperties?.NumberingProperties;
        var numId = numPr?.NumberingId?.Val?.Value;
        var ilvl = numPr?.NumberingLevelReference?.Val?.Value;
        if (numId is > 0)
        {
            var label = numbering.NextLabel(numId.Value, ilvl ?? 0);
            if (label is not null)
                PrependLabel(lines[firstLineIndex], label);
        }
    }

    private static void PrependLabel(Line line, string label)
    {
        if (line.Runs.Count > 0)
        {
            line.Runs[0].Text = label + line.Runs[0].Text;
        }
        else
        {
            line.Runs.Add(new Run { Text = label });
        }
    }

    private static void MergeSegment(Line line, Segment seg)
    {
        foreach (var run in seg.Runs)
            line.Runs.Add(run);
        foreach (var img in seg.Images)
            line.Images.Add(img);
    }

    private static void ReadRun(
        OpenXmlRun r, PartResolver resolver, List<DraftImage> images,
        Action<string, Run> addText, Action<InlineImage> addImage, Action br)
    {
        var fmt = new Run();
        var rPr = r.RunProperties;
        if (rPr is not null)
        {
            // <w:b/> không có val = bật (đặc thù OnOff của OOXML)
            fmt.Bold = ReadOnOff(rPr.Bold);
            fmt.Italic = ReadOnOff(rPr.Italic);
            var underline = rPr.Underline;
            var underlineText = underline?.Val?.InnerText;
            fmt.Underline = underline is not null
                && !string.Equals(underlineText, "none", StringComparison.OrdinalIgnoreCase);
            var vert = rPr.VerticalTextAlignment?.Val?.InnerText;
            fmt.Superscript = string.Equals(vert, "superscript", StringComparison.OrdinalIgnoreCase);
            fmt.Subscript = string.Equals(vert, "subscript", StringComparison.OrdinalIgnoreCase);
            var color = rPr.Color?.Val?.Value;
            fmt.ColorHex = color is { Length: 6 } && !string.Equals(color, "auto", StringComparison.OrdinalIgnoreCase) ? color : null;
            var highlight = rPr.Highlight;
            var highlightText = highlight?.Val?.InnerText;
            fmt.Highlight = highlight is not null ? highlightText ?? "yellow" : null;
        }

        foreach (var child in r.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    addText(t.InnerText, fmt);
                    break;
                case TabChar:
                    addText("\t", fmt);
                    break;
                case Break:
                case CarriageReturn:
                    br();
                    break;
                case Drawing drawing:
                    if (TryExtractBlip(drawing, resolver, images, out var img))
                        addImage(img);
                    break;
                default:
                    // w:object (MathType OLE) / w:pict (VML) — thường là công thức MathType
                    if (child.LocalName is "object" or "pict")
                    {
                        if (TryExtractOleImage(child, resolver, images, out var oleImg))
                            addImage(oleImg);
                        else if (child.LocalName == "object")
                            addText("[Công thức không hiển thị được — hãy thay bằng ảnh PNG]", fmt);
                    }
                    break;
            }
        }
    }

    private static bool TryExtractBlip(Drawing drawing, PartResolver resolver, List<DraftImage> images, out InlineImage img)
    {
        img = null!;
        var blip = drawing.Descendants().FirstOrDefault(e => e.LocalName == "blip");
        if (blip is null)
            return false;
        var relId = blip.GetAttributes()
            .FirstOrDefault(a => a.LocalName == "embed" && a.NamespaceUri == RelationshipsNs)
            .Value;
        var data = relId is not null ? resolver.TryGet(relId) : null;
        if (data is null)
            return false;
        img = new InlineImage
        {
            TempId = $"img-{images.Count}",
            Bytes = data.Value.Bytes,
            ContentType = data.Value.ContentType,
            UnsupportedFormat = data.Value.ContentType is "image/x-wmf" or "image/x-emf" or "image/wmf" or "image/emf",
        };
        return true;
    }

    private static bool TryExtractOleImage(OpenXmlElement element, PartResolver resolver, List<DraftImage> images, out InlineImage img)
    {
        img = null!;
        var relId = element.Descendants()
            .FirstOrDefault(e => e.LocalName == "imagedata")?
            .GetAttributes()
            .FirstOrDefault(a => a.LocalName == "id" && a.NamespaceUri == RelationshipsNs)
            .Value;
        var data = relId is not null ? resolver.TryGet(relId) : null;
        if (data is null)
            return false;
        img = new InlineImage
        {
            TempId = $"img-{images.Count}",
            Bytes = data.Value.Bytes,
            ContentType = data.Value.ContentType,
            UnsupportedFormat = true, // OLE/VML — thường là MathType WMF/EMF
        };
        return true;
    }

    /// <summary>Đọc thuộc tính OnOff: element vắng mặt = false; element không có val = true (đặc thù OOXML).</summary>
    private static bool ReadOnOff(OnOffType? element)
    {
        if (element is null)
            return false;
        var val = element.Val;
        if (val is null)
            return true;
        return val.HasValue ? val.Value : true;
    }

    private static bool SameFormat(Run a, Run b)
        => a.Raw == b.Raw && a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline
           && a.Superscript == b.Superscript && a.Subscript == b.Subscript
           && a.ColorHex == b.ColorHex && a.Highlight == b.Highlight;
}
