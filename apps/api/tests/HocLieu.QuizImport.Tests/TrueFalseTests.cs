using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 11 — câu Đúng/Sai (phương án là Đúng/Sai, có hoặc không có chữ cái).</summary>
public class TrueFalseTests
{
    [Fact]
    public void True_false_question_with_asterisk()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 0,5 = 1/2.")
         .Para("*Đúng")
         .Para("Sai");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.Type.ShouldBe(QuestionType.TrueFalse);
        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q.Options.Count.ShouldBe(2);
        var dung = q.Options.Single(o => o.Label == "Đúng");
        var sai = q.Options.Single(o => o.Label == "Sai");
        dung.IsCorrect.ShouldBeTrue();
        sai.IsCorrect.ShouldBeFalse();
        // Phương án Đúng/Sai lấy chính nhãn làm nội dung (không cảnh báo EMPTY_OPTION)
        dung.ContentHtml.ShouldBe("Đúng");
        sai.ContentHtml.ShouldBe("Sai");
        draft.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void True_false_question_with_answer_line()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 0,75 lớn hơn 0,5.")
         .Para("A. Đúng")
         .Para("B. Sai")
         .Para("Đáp án: A");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        // "A. Đúng" vẫn là phương án chữ cái — không phải dạng Đúng/Sai thuần
        q.Type.ShouldBe(QuestionType.Single);
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
    }
}
