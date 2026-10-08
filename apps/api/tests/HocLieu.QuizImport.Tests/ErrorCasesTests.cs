using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 14 — file lỗi: cảnh báo đúng mã, đúng câu.</summary>
public class ErrorCasesTests
{
    [Fact]
    public void Missing_correct_answer_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para("B. 2")
         .Para("C. 3")
         .Para("D. 4");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.NoCorrectAnswer);
        draft.Warnings.Single(w => w.Code == WarningCodes.NoCorrectAnswer).QuestionNumber.ShouldBe(1);
        draft.Questions.Single().AnswerSource.ShouldBe(AnswerSources.None);
    }

    [Fact]
    public void Question_number_gap_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para("*B. 2")
         .Para("C. 3")
         .Para("Câu 3: 2 + 2 = ?")
         .Para("A. 3")
         .Para("*B. 4")
         .Para("C. 5");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.QuestionNumberGap);
        draft.Warnings.Single(w => w.Code == WarningCodes.QuestionNumberGap).QuestionNumber.ShouldBe(3);
    }

    [Fact]
    public void Duplicate_question_number_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para("*B. 2")
         .Para("C. 3")
         .Para("Câu 1: 2 + 2 = ?")
         .Para("A. 3")
         .Para("*B. 4")
         .Para("C. 5");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.DuplicateQuestionNumber);
        draft.Warnings.Single(w => w.Code == WarningCodes.DuplicateQuestionNumber).QuestionNumber.ShouldBe(1);
    }

    [Fact]
    public void Conflicting_answer_sources_warn_and_keep_highest_priority()
    {
        // *B (Asterisk) + "Đáp án: A" (AnswerLine) → Asterisk thắng, có cảnh báo.
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 5 + 5 = ?")
         .Para("A. 8")
         .Para("*B. 10")
         .Para("C. 12")
         .Para("Đáp án: A");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.ConflictingAnswerSources);
        q.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public void Answer_key_entry_without_matching_question_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 1")
         .Para("*B. 2")
         .Para("C. 3")
         .Para("ĐÁP ÁN")
         .Para("9.C");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.AnswerKeyUnmatched);
        draft.Warnings.Single(w => w.Code == WarningCodes.AnswerKeyUnmatched).QuestionNumber.ShouldBe(9);
        // Câu 1 vẫn giữ đáp án từ dấu *
        draft.Questions.Single().AnswerSource.ShouldBe(AnswerSources.Asterisk);
    }

    [Fact]
    public void Too_few_options_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 1 + 1 = ?")
         .Para("A. 2");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.TooFewOptions);
    }

    [Fact]
    public void Empty_option_warns()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: Chọn số đúng:")
         .Para("A. 5")
         .Para("B.")
         .Para("C. 7")
         .Para("Đáp án: A");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.EmptyOption);
        q.Options.Single(o => o.Label == "B").ContentHtml.ShouldBe(string.Empty);
    }

    [Fact]
    public void Duplicate_option_label_warns()
    {
        // Hai phương án "Đúng"/"đúng" — trùng sau khi chuẩn hóa chữ hoa.
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: 0,5 = 1/2.")
         .Para("Đúng")
         .Para("đúng");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.DuplicateOptionLabel);
    }
}
