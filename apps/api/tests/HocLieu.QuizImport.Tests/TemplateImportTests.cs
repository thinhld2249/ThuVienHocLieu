using ClosedXML.Excel;
using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>
/// File mẫu của hệ thống phải nhập đúng 100% (spec §6.3): không cảnh báo,
/// đủ câu, đủ đáp án. Chạy trên file thật trong apps/web/public/templates.
/// </summary>
public class TemplateImportTests
{
    private static string TemplatesDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "web", "public", "templates"));

    private static string Template(string name)
    {
        var path = Path.Combine(TemplatesDir, name);
        File.Exists(path).ShouldBeTrue($"Thiếu file mẫu {path} — chạy: dotnet run --project apps/api/tools/TemplateGen");
        return path;
    }

    [Fact]
    public void Docx_template_imports_with_zero_warnings()
    {
        var draft = QuizImporter.ParseDocx(File.OpenRead(Template("mau-bai-tap.docx")));

        draft.Warnings.ShouldBeEmpty();
        draft.Questions.Count.ShouldBe(6);
        draft.Groups.Count.ShouldBe(2);
        draft.Title.ShouldBe("BÀI TẬP CUỐI TUẦN 5 – TOÁN 5");
        draft.DescriptionHtml!.ShouldContain("BÀI TẬP CUỐI TUẦN 5 – TOÁN 5");

        var q1 = draft.Questions.Single(q => q.Number == 1);
        q1.AnswerSource.ShouldBe(AnswerSources.Asterisk);
        q1.Options.Count.ShouldBe(4);
        q1.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q1.Options.Single(o => o.Label == "B").ContentHtml.ShouldBe("5,7");

        var q2 = draft.Questions.Single(q => q.Number == 2);
        q2.AnswerSource.ShouldBe(AnswerSources.AnswerLine);
        q2.Options.Single(o => o.Label == "A").IsCorrect.ShouldBeTrue();
        q2.ExplanationHtml!.ShouldContain("Nhân cả tử và mẫu của 3/4 với 2 được 6/8.");

        var q3 = draft.Questions.Single(q => q.Number == 3);
        q3.Type.ShouldBe(QuestionType.Multi);
        q3.Options.Count(o => o.IsCorrect).ShouldBe(2);

        var q4 = draft.Questions.Single(q => q.Number == 4);
        q4.Type.ShouldBe(QuestionType.TrueFalse);
        q4.Options.Single(o => o.Label == "Đúng").IsCorrect.ShouldBeTrue();

        // 2 câu cuối nhận đáp án từ khối ĐÁP ÁN cuối file
        var q5 = draft.Questions.Single(q => q.Number == 5);
        q5.AnswerSource.ShouldBe(AnswerSources.AnswerKey);
        q5.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
        q5.GroupTempId.ShouldBe(draft.Groups[1].TempId);

        var q6 = draft.Questions.Single(q => q.Number == 6);
        q6.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();

        // Nhóm 2 có đoạn dẫn
        draft.Groups[0].Title.ShouldBe("PHẦN I. TRẮC NGHIỆM");
        draft.Groups[1].Title.ShouldBe("PHẦN II. ĐỌC HIỂU");
        draft.Groups[0].PassageHtml.ShouldBeNull();
        draft.Groups[1].PassageHtml!.ShouldContain("Mùa thu, bầu trời như cao hơn");
    }

    [Fact]
    public void Xlsx_template_imports_with_zero_warnings()
    {
        var draft = QuizImporter.ParseXlsx(File.OpenRead(Template("mau-bai-tap.xlsx")));

        draft.Warnings.ShouldBeEmpty();
        draft.Questions.Count.ShouldBe(6);
        draft.Groups.Count.ShouldBe(1);

        // "5,7" ghi dạng text phải giữ nguyên dấu phẩy (không bị đọc thành "5.7")
        var q1 = draft.Questions.Single(q => q.Number == 1);
        q1.Options.Single(o => o.Label == "B").ContentHtml.ShouldBe("5,7");
        q1.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();

        var q3 = draft.Questions.Single(q => q.Number == 3);
        q3.Type.ShouldBe(QuestionType.Multi);
        q3.Options.Count(o => o.IsCorrect).ShouldBe(2);

        var q4 = draft.Questions.Single(q => q.Number == 4);
        q4.Type.ShouldBe(QuestionType.TrueFalse);
        q4.Options.Single(o => o.Label == "Đúng").IsCorrect.ShouldBeTrue();

        var q5 = draft.Questions.Single(q => q.Number == 5);
        q5.GroupTempId.ShouldBe(draft.Groups[0].TempId);
        q5.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();

        var q6 = draft.Questions.Single(q => q.Number == 6);
        q6.GroupTempId.ShouldBe(draft.Groups[0].TempId); // cột Nhóm trống → giữ nhóm cũ
        q6.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();

        draft.Groups[0].Title.ShouldBe("PHẦN II. ĐỌC HIỂU");
        draft.Groups[0].PassageHtml!.ShouldContain("Mùa thu, bầu trời như cao hơn");

        draft.Questions.All(q => q.Points == 1m).ShouldBeTrue();
    }

    [Fact]
    public void Hs_template_has_expected_columns()
    {
        var path = Template("mau-danh-sach-hs.xlsx");
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var headers = ws.Row(1).CellsUsed().Select(c => c.GetString().Trim()).ToList();
        headers.ShouldBe(new[] { "STT", "Họ và tên", "Ngày sinh", "Giới tính", "Mã HS" });
        ws.LastRowUsed()?.RowNumber().ShouldBe(1); // chỉ có hàng tiêu đề, không có dòng mẫu
    }
}
