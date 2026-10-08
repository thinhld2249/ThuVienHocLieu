using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 2 — dòng <c>Đáp án: A</c> sau các phương án + dòng giải thích.</summary>
public class AnswerLineTests
{
    [Fact]
    public void Answer_line_marks_correct_option_and_explanation_is_captured()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Phân số nào bằng 3/4?")
         .Para("A. 6/8")
         .Para("B. 4/3")
         .Para("C. 3/8")
         .Para("D. 9/16")
         .Para("Đáp án: A")
         .Para("Giải thích: Nhân cả tử và mẫu của 3/4 với 2 được 6/8.");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        var q = draft.Questions.Single();
        q.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeFalse();
        q.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeFalse();
        q.Options.Single(o => o.Label == "D").IsCorrect.ShouldBeFalse();
        q.ExplanationHtml!.ShouldContain("Nhân cả tử và mẫu của 3/4 với 2 được 6/8.");
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Answer_line_with_multiple_letters_marks_multi()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Những số nào chẵn?")
         .Para("A. 1")
         .Para("B. 2")
         .Para("C. 4")
         .Para("D. 5")
         .Para("Đáp án: B, C");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Type.ShouldBe(QuestionType.Multi);
        q.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeFalse();
        q.Options.Single(o => o.Label == "D").IsCorrect.ShouldBeFalse();
    }
}
