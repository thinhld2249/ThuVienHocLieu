using ClosedXML.Excel;
using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>Kịch bản cơ bản cho XlsxQuizReader (spec §6.4).</summary>
public class XlsxImportTests
{
    private static readonly string[] StdHeaders =
        { "STT", "Câu hỏi", "A", "B", "C", "D", "Đáp án", "Giải thích", "Nhóm", "Đoạn văn", "Điểm" };

    private static QuizDraft Parse(Action<IXLWorksheet> fill)
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("S1");
            for (var i = 0; i < StdHeaders.Length; i++)
                ws.Cell(1, i + 1).Value = StdHeaders[i];
            fill(ws);
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return QuizImporter.ParseXlsx(ms);
    }

    private static void Row(IXLWorksheet ws, int r, int stt, string question, string a, string b,
        string? answer = null, string? explanation = null, string? group = null, string? passage = null, string? points = null)
    {
        ws.Cell(r, 1).Value = stt;
        ws.Cell(r, 2).Value = question;
        ws.Cell(r, 3).Value = a;
        ws.Cell(r, 4).Value = b;
        if (answer is not null) ws.Cell(r, 7).Value = answer;
        if (explanation is not null) ws.Cell(r, 8).Value = explanation;
        if (group is not null) ws.Cell(r, 9).Value = group;
        if (passage is not null) ws.Cell(r, 10).Value = passage;
        if (points is not null) ws.Cell(r, 11).Value = points;
    }

    [Fact]
    public void Vietnamese_comma_in_text_option_is_preserved()
    {
        var draft = Parse(ws =>
        {
            ws.Cell(2, 2).Value = "Số thập phân 5,7 viết là?";
            ws.Cell(2, 3).Value = "5,7";   // text — giữ dấu phẩy
            ws.Cell(2, 4).Value = "5,07";
            ws.Cell(2, 7).Value = "A";
        });

        draft.Warnings.ShouldBeEmpty();
        var q = draft.Questions.Single();
        q.ContentHtml.ShouldContain("5,7 viết là?");
        q.Options.Single(o => o.Label == "A").ContentHtml.ShouldBe("5,7");
        q.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void Multiple_answers_produce_multi_question()
    {
        var draft = Parse(ws =>
        {
            Row(ws, 2, 1, "Chọn các số nguyên tố:", "2", "4", answer: "A, C");
            ws.Cell(2, 5).Value = "7";
        });

        var q = draft.Questions.Single();
        q.Type.ShouldBe(QuestionType.Multi);
        q.Options.Count(o => o.IsCorrect).ShouldBe(2);
        q.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
    }

    [Fact]
    public void Missing_answer_produces_no_correct_warning()
    {
        var draft = Parse(ws => Row(ws, 2, 1, "Câu thiếu đáp án", "x", "y"));

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.NoCorrectAnswer);
        draft.Warnings.Single(w => w.Code == WarningCodes.NoCorrectAnswer).QuestionNumber.ShouldBe(1);
        draft.Questions.Single().AnswerSource.ShouldBe(AnswerSources.None);
    }

    [Fact]
    public void Stt_gap_and_duplicate_produce_warnings()
    {
        var draft = Parse(ws =>
        {
            Row(ws, 2, 1, "Câu 1", "a", "b", answer: "A");
            Row(ws, 3, 1, "Câu 1 trùng", "a", "b", answer: "A");
            Row(ws, 4, 4, "Câu 4", "a", "b", answer: "A");
        });

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.DuplicateQuestionNumber);
        draft.Warnings.Single(w => w.Code == WarningCodes.DuplicateQuestionNumber).QuestionNumber.ShouldBe(1);
        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.QuestionNumberGap);
        draft.Warnings.Single(w => w.Code == WarningCodes.QuestionNumberGap).QuestionNumber.ShouldBe(4);
    }

    [Fact]
    public void Missing_question_column_is_rejected()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("S1");
            ws.Cell(1, 1).Value = "A";
            ws.Cell(1, 2).Value = "B";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var act = () => QuizImporter.ParseXlsx(ms);
        var ex = act.ShouldThrow<ImportException>();
        ex.Message.ShouldContain("Câu hỏi");
    }

    [Fact]
    public void Fewer_than_two_option_columns_is_rejected()
    {
        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("S1");
            ws.Cell(1, 1).Value = "Câu hỏi";
            ws.Cell(1, 2).Value = "A";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var act = () => QuizImporter.ParseXlsx(ms);
        var ex = act.ShouldThrow<ImportException>();
        ex.Message.ShouldContain("ít nhất 2 cột phương án");
    }

    [Fact]
    public void Empty_rows_are_skipped_without_gap_warning()
    {
        var draft = Parse(ws =>
        {
            Row(ws, 2, 1, "Câu 1", "a", "b", answer: "A");
            // row 3 bỏ trống
            Row(ws, 4, 2, "Câu 2", "a", "b", answer: "B");
        });

        draft.Warnings.ShouldBeEmpty();
        draft.Questions.Count.ShouldBe(2);
        draft.Questions.Select(q => q.Number).ShouldBe(new int?[] { 1, 2 });
    }

    [Fact]
    public void Group_with_passage_is_created_and_reused()
    {
        var draft = Parse(ws =>
        {
            Row(ws, 2, 1, "Câu đầu nhóm", "a", "b", answer: "A", group: "PHẦN I. TRẮC NGHIỆM", passage: "Đọc kỹ đề trước.");
            Row(ws, 3, 2, "Câu cùng nhóm", "a", "b", answer: "B");
        });

        draft.Groups.Count.ShouldBe(1);
        draft.Groups[0].Title.ShouldBe("PHẦN I. TRẮC NGHIỆM");
        draft.Groups[0].PassageHtml!.ShouldContain("Đọc kỹ đề trước.");
        draft.Questions.All(q => q.GroupTempId == draft.Groups[0].TempId).ShouldBeTrue();
    }

    [Fact]
    public void Points_column_with_comma_decimal_is_parsed()
    {
        var draft = Parse(ws => Row(ws, 2, 1, "Câu 1 điểm 1,5", "a", "b", answer: "A", points: "1,5"));

        draft.Questions.Single().Points.ShouldBe(1.5m);
    }

    [Fact]
    public void Empty_option_selected_as_answer_produces_empty_option_warning()
    {
        var draft = Parse(ws => Row(ws, 2, 1, "Câu 1", "x", "", answer: "B"));

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.EmptyOption);
        draft.Warnings.Single(w => w.Code == WarningCodes.EmptyOption).QuestionNumber.ShouldBe(1);
    }

    [Fact]
    public void True_false_question_with_sai_answer()
    {
        var draft = Parse(ws => Row(ws, 2, 1, "2 + 2 = 5.", "Đúng", "Sai", answer: "Sai"));

        var q = draft.Questions.Single();
        q.Type.ShouldBe(QuestionType.TrueFalse);
        q.Options.Single(o => o.Label == "Sai").IsCorrect.ShouldBeTrue();
        q.Options.Single(o => o.Label == "Đúng").IsCorrect.ShouldBeFalse();
    }
}
