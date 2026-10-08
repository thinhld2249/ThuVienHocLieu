using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 12 — câu chọn nhiều (nhiều hơn 1 đáp án đúng → Type = Multi).</summary>
public class MultiSelectTests
{
    [Fact]
    public void Multiple_correct_options_produce_multi_question()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Những số nào lớn hơn 2,5? (chọn nhiều đáp án)")
         .Para("*A. 2,51")
         .Para("B. 2,05")
         .Para("*C. 3")
         .Para("D. 2,499");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Type.ShouldBe(QuestionType.Multi);
        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeFalse();
        q.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "D").IsCorrect.ShouldBeFalse();
        draft.Warnings.ShouldBeEmpty();
    }
}
