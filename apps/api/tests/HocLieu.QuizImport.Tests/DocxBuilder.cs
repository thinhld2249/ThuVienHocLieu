using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Math;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;
using MathRun = DocumentFormat.OpenXml.Math.Run;
using OfficeMath = DocumentFormat.OpenXml.Math.OfficeMath;
using MathText = DocumentFormat.OpenXml.Math.Text;
using OpenXmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using Pics = DocumentFormat.OpenXml.Drawing.Pictures;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using WDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using WRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using WText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace HocLieu.QuizImport.Tests;

/// <summary>
/// Test helper: dựng .docx trong bộ nhớ (OpenXml 3.x) cho các kịch bản nhập quiz §6.3.
/// Text luôn ghi với Space=Preserve; '\t' trong text → &lt;w:tab/&gt;.
/// Ảnh là media part thật (rId trong word/_rels/document.xml.rels).
/// </summary>
public sealed class DocxBuilder : IDisposable
{
    private readonly MemoryStream _ms;
    private readonly WordprocessingDocument _doc;
    private readonly MainDocumentPart _main;
    private readonly Body _body;
    private int _picCount;

    private DocxBuilder()
    {
        _ms = new MemoryStream();
        _doc = WordprocessingDocument.Create(_ms, WordprocessingDocumentType.Document);
        _main = _doc.AddMainDocumentPart()!;
        _body = new Body();
        _main.Document = new Document(_body);
    }

    public static DocxBuilder Create() => new();

    /// <summary>Đoạn văn; nội dung: string, RunSpec, ImageSpec hoặc OfficeMath (OMML).</summary>
    public DocxBuilder Para(params object[] content)
    {
        var p = new WParagraph();
        AppendContent(p, content);
        _body.Append(p);
        return this;
    }

    /// <summary>
    /// Đoạn đánh số tự động (numbering.xml) — parser sẽ nhét nhãn ("A. ", "1. ") vào đầu dòng.
    /// </summary>
    public DocxBuilder NumberedPara(int numId, int ilvl, params object[] content)
    {
        var p = new WParagraph(
            new WParagraphProperties(
                new NumberingProperties(
                    new NumberingLevelReference { Val = ilvl },
                    new NumberingId { Val = numId })));
        AppendContent(p, content);
        _body.Append(p);
        return this;
    }

    /// <summary>Bảng; mỗi ô là một đoạn văn (chữ, '\t' → tab).</summary>
    public DocxBuilder Table(params string[][] rows)
    {
        var table = new Table(new TableProperties());
        foreach (var row in rows)
        {
            var tr = new TableRow();
            foreach (var cell in row)
            {
                // <w:t> phải nằm trong <w:r> — file Word thật luôn có run wrapper
                var run = new OpenXmlRun();
                foreach (var el in BuildTextChildren(cell))
                    run.Append(el);
                tr.Append(new TableCell(new WParagraph(run)));
            }
            table.Append(tr);
        }
        _body.Append(table);
        return this;
    }

    /// <summary>
    /// Khai báo danh sách đánh số: numId 1-based; numFmt: decimal|upperLetter|lowerLetter;
    /// lvlText dùng token %1 (vd "decimal" + "%1." → "1. ", "upperLetter" + "%1." → "A. ").
    /// </summary>
    public void DefineNumbering(int numId, string numFmt, string lvlText)
    {
        var part = _main.NumberingDefinitionsPart ?? _main.AddNewPart<NumberingDefinitionsPart>();
        var numbering = part.Numbering ?? new Numbering();
        var abstractId = numId - 1;
        numbering.Append(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Format = numFmt },
                    new LevelText { Val = lvlText })
                { LevelIndex = 0 })
            { AbstractNumberId = abstractId });
        numbering.Append(new NumberingInstance(new AbstractNumId { Val = abstractId }) { NumberID = numId });
        part.Numbering = numbering;
    }

    public byte[] ToBytes()
    {
        _doc.Save();
        return _ms.ToArray();
    }

    public Stream ToStream() => new MemoryStream(ToBytes(), writable: false);

    public void Dispose()
    {
        _doc.Dispose();
        _ms.Dispose();
    }

    private void AppendContent(WParagraph p, object[] content)
    {
        foreach (var item in content)
        {
            switch (item)
            {
                case string s:
                    p.Append(MakeRun(new RunSpec(s)));
                    break;
                case RunSpec rs:
                    p.Append(MakeRun(rs));
                    break;
                case ImageSpec img:
                    p.Append(new OpenXmlRun(MakeDrawing(img)));
                    break;
                case OfficeMath math:
                    p.Append(math);
                    break;
                default:
                    throw new ArgumentException($"Không hỗ trợ nội dung {item.GetType().Name} trong Para().");
            }
        }
    }

    private static IEnumerable<OpenXmlElement> BuildTextChildren(string text)
    {
        var parts = text.Split('\t');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                yield return new TabChar();
            if (parts[i].Length > 0)
                yield return new WText(parts[i]) { Space = SpaceProcessingModeValues.Preserve };
        }
    }

    private static OpenXmlRun MakeRun(RunSpec rs)
    {
        var props = new List<OpenXmlElement>();
        if (rs.Bold) props.Add(new Bold());
        if (rs.Italic) props.Add(new Italic());
        if (rs.Color is not null) props.Add(new Color { Val = rs.Color });
        if (rs.Highlight is not null) props.Add(new Highlight { Val = MapHighlight(rs.Highlight) });
        if (rs.Underline) props.Add(new Underline { Val = UnderlineValues.Single });
        if (rs.Superscript) props.Add(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript });
        if (rs.Subscript) props.Add(new VerticalTextAlignment { Val = VerticalPositionValues.Subscript });

        var run = new OpenXmlRun();
        if (props.Count > 0)
            run.Append(new WRunProperties(props));
        foreach (var el in BuildTextChildren(rs.Text))
            run.Append(el);
        return run;
    }

    private static HighlightColorValues MapHighlight(string h) => h.ToLowerInvariant() switch
    {
        "yellow" => HighlightColorValues.Yellow,
        "green" => HighlightColorValues.Green,
        "cyan" => HighlightColorValues.Cyan,
        "magenta" => HighlightColorValues.Magenta,
        "red" => HighlightColorValues.Red,
        _ => HighlightColorValues.Yellow
    };

    private WDrawing MakeDrawing(ImageSpec img)
    {
        _picCount++;
        var partType = img.ContentType switch
        {
            "image/png" => ImagePartType.Png,
            "image/jpeg" or "image/jpg" => ImagePartType.Jpeg,
            "image/gif" => ImagePartType.Gif,
            "image/bmp" => ImagePartType.Bmp,
            "image/x-emf" or "image/emf" => ImagePartType.Emf,
            "image/x-wmf" or "image/wmf" => ImagePartType.Wmf,
            _ => ImagePartType.Png
        };
        var imagePart = _main.AddImagePart(partType);
        using (var s = imagePart.GetStream(FileMode.Create))
            s.Write(img.Bytes, 0, img.Bytes.Length);
        var relId = _main.GetIdOfPart(imagePart);
        var picId = (uint)_picCount;
        const long oneInch = 914400; // EMU
        return new WDrawing(
            new Wp.Inline(
                new Wp.Extent { Cx = oneInch, Cy = oneInch },
                new Wp.DocProperties { Id = picId, Name = $"Picture {_picCount}" },
                new Drawing.Graphic(
                    new Drawing.GraphicData(
                        new Pics.Picture(
                            new Pics.NonVisualPictureProperties(
                                new Pics.NonVisualDrawingProperties { Id = picId, Name = $"Picture {_picCount}" },
                                new Pics.NonVisualPictureDrawingProperties()
                            ),
                            new Pics.BlipFill(
                                new Drawing.Blip { Embed = relId }
                            )
                        )
                    )
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            ));
    }
}

/// <summary>Run chữ kèm định dạng (định dạng = nguồn đáp án Formatting, §6.3.8d).</summary>
public sealed record RunSpec(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    string? Color = null,
    string? Highlight = null,
    bool Superscript = false,
    bool Subscript = false)
{
    public static implicit operator RunSpec(string text) => new(text);
}

/// <summary>Ảnh nhúng vào docx (media part thật với rId).</summary>
public sealed record ImageSpec(byte[] Bytes, string ContentType);

/// <summary>Helper OMML (công thức Word) cho test §6.3.13.</summary>
public static class Mathx
{
    public static OfficeMath Txt(string text) => new(new MathRun(new MathText(text)));

    public static OfficeMath Cat(params OfficeMath[] parts) => new(parts);

    public static OfficeMath Frac(string num, string den)
        => new(new Fraction(
            new Numerator(new MathRun(new MathText(num))),
            new Denominator(new MathRun(new MathText(den)))));

    public static OfficeMath Frac(OfficeMath num, OfficeMath den)
        => new(new Fraction(new Numerator(num), new Denominator(den)));

    public static OfficeMath Sup(string baseText, string sup)
        => new(new Superscript(
            new Base(new MathRun(new MathText(baseText))),
            new SuperArgument(new MathRun(new MathText(sup)))));

    public static OfficeMath Sub(string baseText, string sub)
        => new(new Subscript(
            new Base(new MathRun(new MathText(baseText))),
            new SubArgument(new MathRun(new MathText(sub)))));

    public static OfficeMath Rad(string body)
        => new(new Radical(new Base(new MathRun(new MathText(body)))));
}
