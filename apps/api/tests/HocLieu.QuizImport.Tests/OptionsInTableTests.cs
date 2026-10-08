using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 7 — phương án nằm trong bảng (vd bảng 2x2).</summary>
public class OptionsInTableTests
{
    [Fact]
    public void Options_in_2x2_table_are_parsed()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:")
         .Table(new[] { "A. 5,07", "*B. 5,7" }, new[] { "C. 57", "D. 0,57" });

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
}
