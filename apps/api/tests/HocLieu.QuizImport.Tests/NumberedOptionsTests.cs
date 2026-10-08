using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 8 — phương án đánh số tự động (numbering.xml upperLetter → "A. ", "B. "…).</summary>
public class NumberedOptionsTests
{
    [Fact]
    public void Auto_numbered_options_get_letter_labels_and_answer_key_applies()
    {
        using var b = DocxBuilder.Create();
        b.DefineNumbering(1, "upperLetter", "%1.");
        b.Para("Câu 1: Số nào là cách viết của 5,7?")
         .NumberedPara(1, 0, "5,07")
         .NumberedPara(1, 0, "5,7")
         .NumberedPara(1, 0, "57")
         .NumberedPara(1, 0, "0,57")
         .Para("ĐÁP ÁN")
         .Para("1.B");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Options.Count.ShouldBe(4);
        q.Options.Select(o => o.Label).ShouldBe(new[] { "A", "B", "C", "D" });
        q.Options[0].ContentHtml.ShouldBe("5,07");
        q.Options[1].ContentHtml.ShouldBe("5,7");
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Lower_letter_numbering_produces_lowercase_labels()
    {
        using var b = DocxBuilder.Create();
        b.DefineNumbering(2, "lowerLetter", "%1)");
        b.Para("Câu 1: 2 + 3 = ?")
         .NumberedPara(2, 0, "4")
         .NumberedPara(2, 0, "5")
         .NumberedPara(2, 0, "6")
         .Para("Đáp án: b");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Options.Count.ShouldBe(3);
        q.Options.Select(o => o.Label).ShouldBe(new[] { "A", "B", "C" });
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
    }
}
