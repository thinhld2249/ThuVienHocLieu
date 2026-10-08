using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 1 — đáp án đánh dấu <c>*</c> ngay trước chữ cái phương án.</summary>
public class AsteriskAnswerTests
{
    [Fact]
    public void Asterisk_before_label_marks_correct_option()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:")
         .Para("A. 5,07")
         .Para("*B. 5,7")
         .Para("C. 57")
         .Para("D. 0,57");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Questions.Count.ShouldBe(1);
        var q = draft.Questions[0];
        q.Number.ShouldBe(1);
        q.Type.ShouldBe(QuestionType.Single);
        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q.ContentHtml.ShouldContain("Số thập phân gồm 5 đơn vị, 7 phần mười viết là");
        q.Options.Count.ShouldBe(4);
        q.Options[0].Label.ShouldBe("A");
        q.Options[0].ContentHtml.ShouldBe("5,07");
        q.Options[0].IsCorrect.ShouldBeFalse();
        q.Options[1].Label.ShouldBe("B");
        q.Options[1].ContentHtml.ShouldBe("5,7");
        q.Options[1].IsCorrect.ShouldBeTrue();
        q.Options[2].IsCorrect.ShouldBeFalse();
        q.Options[3].IsCorrect.ShouldBeFalse();
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Asterisk_with_space_before_label_is_still_detected()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para(" *B. 2")
         .Para("C. 3");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.Options.Count.ShouldBe(3);
    }
}
