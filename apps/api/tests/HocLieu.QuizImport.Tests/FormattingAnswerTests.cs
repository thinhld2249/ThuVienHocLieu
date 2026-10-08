using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 5 — đáp án đúng theo định dạng: gạch chân / chữ đỏ / in đậm duy nhất.</summary>
public class FormattingAnswerTests
{
    [Fact]
    public void Underlined_option_is_correct()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 3/4 bằng số thập phân nào?")
         .Para("A. 0,34")
         .Para(new RunSpec("B. 0,75", Underline: true))
         .Para("C. 7,5")
         .Para("D. 34");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.Formatting);
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeFalse();
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Red_option_is_correct()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Số nào lớn hơn 2,5?")
         .Para("A. 2,4")
         .Para("B. 2,49")
         .Para(new RunSpec("C. 3", Color: "FF0000"))
         .Para("D. 2,5");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.Formatting);
        q.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Only_bold_option_is_correct()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 5 + 7 = ?")
         .Para("A. 10")
         .Para("B. 11")
         .Para(new RunSpec("C. 12", Bold: true))
         .Para("D. 13");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.Formatting);
        q.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Content_only_underline_after_plain_label_is_detected()
    {
        // Nhãn "B." thường, chỉ phần nội dung " 5,7" gạch chân → vẫn nhận diện được (CloneForText theo vị trí nội dung).
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Chọn số đúng:")
         .Para("A. 5,07")
         .Para(new RunSpec("B."), new RunSpec(" 5,7", Underline: true))
         .Para("C. 57")
         .Para("D. 0,57");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.Formatting);
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        // định dạng gạch chân được giữ trong HTML phương án
        q.Options.Single(o => o.Label == "B").ContentHtml.ShouldBe("<u>5,7</u>");
        draft.Warnings.ShouldBeEmpty();
    }
}
