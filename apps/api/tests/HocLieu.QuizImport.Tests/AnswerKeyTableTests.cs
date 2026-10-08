using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 4 — khối đáp án dạng bảng 2 hàng (<c>Câu | 5 | 6</c> / <c>Đáp án | C | B</c>).</summary>
public class AnswerKeyTableTests
{
    [Fact]
    public void Two_row_answer_key_table_applies_answers_per_column()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 5: Đoạn văn tả cảnh mùa nào?")
         .Para("A. Mùa xuân")
         .Para("B. Mùa hạ")
         .Para("C. Mùa thu")
         .Para("D. Mùa đông")
         .Para("Câu 6: Từ nào là danh từ riêng?")
         .Para("A. bầu trời")
         .Para("B. Hà Nội")
         .Para("C. cao")
         .Para("D. như")
         .Para("ĐÁP ÁN")
         .Table(new[] { "Câu", "5", "6" }, new[] { "Đáp án", "C", "B" });

        var draft = QuizImporter.ParseDocx(b.ToStream());

        var q5 = draft.Questions.Single(q => q.Number == 5);
        var q6 = draft.Questions.Single(q => q.Number == 6);
        q5.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q5.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        q5.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeFalse();
        q6.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q6.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q6.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeFalse();
        draft.Warnings.ShouldBeEmpty();
    }
}
