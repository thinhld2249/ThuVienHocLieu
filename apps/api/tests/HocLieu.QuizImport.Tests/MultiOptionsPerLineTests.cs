using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 6 — nhiều phương án trên 1 dòng (tab hoặc ≥ 2 dấu cách).</summary>
public class MultiOptionsPerLineTests
{
    [Fact]
    public void Tab_separated_options_on_one_line()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:")
         .Para("A. 5,07\t*B. 5,7\tC. 57\tD. 0,57");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Options.Count.ShouldBe(4);
        q.Options.Select(o => o.Label).ShouldBe(new[] { "A", "B", "C", "D" });
        q.Options[0].ContentHtml.ShouldBe("5,07");
        q.Options[1].ContentHtml.ShouldBe("5,7");
        q.Options[2].ContentHtml.ShouldBe("57");
        q.Options[3].ContentHtml.ShouldBe("0,57");
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Two_space_separated_options_on_one_line()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Chọn phân số bằng 3/4:")
         .Para("A. 6/8    B. 4/3    C. 3/8    D. 9/16")
         .Para("Đáp án: A");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Options.Count.ShouldBe(4);
        q.Options.Select(o => o.Label).ShouldBe(new[] { "A", "B", "C", "D" });
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
        q.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
        draft.Warnings.ShouldBeEmpty();
    }
}
