using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 9 — ảnh trong đề và trong phương án; WMF/EMF → cảnh báo.</summary>
public class ImageTests
{
    // PNG 1x1 trong suốt
    private static byte[] Png()
        => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void Images_in_stem_and_option_get_temp_ids()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Chọn đáp án đúng: ", new ImageSpec(Png(), "image/png"))
         .Para("A. 5,07")
         .Para("B. Xem hình", new ImageSpec(Png(), "image/png"))
         .Para("C. 57")
         .Para("D. 0,57")
         .Para("Đáp án: B");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Images.Count.ShouldBe(2);
        draft.Images[0].TempId.ShouldBe("img-0");
        draft.Images[1].TempId.ShouldBe("img-1");
        draft.Images[0].ContentType.ShouldBe("image/png");
        draft.Images[0].Bytes.ShouldBe(Png());

        var q = draft.Questions.Single();
        q.ContentHtml.ShouldContain("data-temp-id=\"img-0\"");
        q.Options.Single(o => o.Label == "B").ContentHtml.ShouldContain("data-temp-id=\"img-1\"");
        q.Options.Single(o => o.Label == "B").ContentHtml.ShouldContain("Xem hình");
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void Emf_image_produces_unsupported_format_warning()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Quan sát hình dưới đây", new ImageSpec(new byte[] { 0x01, 0x00, 0x00, 0x00 }, "image/x-emf"))
         .Para("A. Hình tròn")
         .Para("B. Hình vuông")
         .Para("C. Hình tam giác");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.UnsupportedImageFormat);
        var q = draft.Questions.Single();
        q.ContentHtml.ShouldContain("[Ảnh chưa hiển thị được");
        draft.Images.Single().ContentType.ShouldBe("image/x-emf");
    }
}
