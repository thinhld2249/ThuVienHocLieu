using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 3 — khối đáp án cuối file dạng text (<c>1.C 2.B</c>).</summary>
public class AnswerKeyTextTests
{
    [Fact]
    public void Answer_key_block_applies_answers_to_all_questions()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para("B. 2")
         .Para("C. 3")
         .Para("D. 4")
         .Para("Câu 2: 2 x 3 = ?")
         .Para("A. 5")
         .Para("B. 6")
         .Para("C. 7")
         .Para("D. 8")
         .Para("ĐÁP ÁN")
         .Para("1.C  2.B");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Questions.Count.ShouldBe(2);
        var q1 = draft.Questions[0];
        var q2 = draft.Questions[1];
        q1.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q1.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        q1.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeFalse();
        q2.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q2.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q2.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeFalse();
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Answer_key_with_dash_separator_is_parsed()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Trái đất quay quanh ?")
         .Para("A. Mặt Trời")
         .Para("B. Mặt Trăng")
         .Para("C. Kim tinh")
         .Para("ĐÁP ÁN")
         .Para("1-A");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
        draft.Warnings.ShouldBeEmpty();
    }
}
