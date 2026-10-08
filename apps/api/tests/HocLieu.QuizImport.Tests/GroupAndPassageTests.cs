using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 10 — nhóm (PHẦN …) + đoạn văn dẫn (passage) + tiêu đề từ preamble.</summary>
public class GroupAndPassageTests
{
    [Fact]
    public void Groups_passage_and_title_are_parsed()
    {
        using var b = DocxBuilder.Create();
        b.Para("BÀI TẬP CUỐI TUẦN 5 – TOÁN 5")
         .Para("PHẦN I. TRẮC NGHIỆM")
         .Para("Câu 1: Số 5,7 viết bằng phân số nào?")
         .Para("A. 57/100")
         .Para("*B. 57/10")
         .Para("C. 57/1000")
         .Para("D. 57/10000")
         .Para("PHẦN II. ĐỌC HIỂU")
         .Para("Đọc đoạn văn sau và trả lời câu 2 và 3:")
         .Para("Mùa thu, bầu trời như cao hơn.")
         .Para("Câu 2: Đoạn văn tả cảnh mùa nào?")
         .Para("A. Mùa xuân")
         .Para("B. Mùa hạ")
         .Para("*C. Mùa thu")
         .Para("D. Mùa đông")
         .Para("Câu 3: Từ nào trong đoạn là tính từ?")
         .Para("A. bầu trời")
         .Para("B. cao")
         .Para("C. mùa thu")
         .Para("D. như")
         .Para("Đáp án: B");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        // Tiêu đề = dòng đầu tiên trước câu hỏi đầu tiên
        draft.Title.ShouldBe("BÀI TẬP CUỐI TUẦN 5 – TOÁN 5");

        // Hai nhóm, đúng thứ tự
        draft.Groups.Count.ShouldBe(2);
        draft.Groups[0].TempId.ShouldBe("g1");
        draft.Groups[0].Title.ShouldBe("PHẦN I. TRẮC NGHIỆM");
        draft.Groups[1].TempId.ShouldBe("g2");
        draft.Groups[1].Title.ShouldBe("PHẦN II. ĐỌC HIỂU");

        // Đoạn văn của nhóm 2 gồm cả dòng dẫn và nội dung
        draft.Groups[1].PassageHtml!.ShouldContain("Đọc đoạn văn sau và trả lời câu 2 và 3:");
        draft.Groups[1].PassageHtml!.ShouldContain("Mùa thu, bầu trời như cao hơn.");
        draft.Groups[0].PassageHtml.ShouldBeNull();

        // Câu hỏi gắn đúng nhóm
        var q1 = draft.Questions.Single(q => q.Number == 1);
        var q2 = draft.Questions.Single(q => q.Number == 2);
        var q3 = draft.Questions.Single(q => q.Number == 3);
        q1.GroupTempId.ShouldBe("g1");
        q2.GroupTempId.ShouldBe("g2");
        q3.GroupTempId.ShouldBe("g2");

        q1.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        q2.Options.Single(o => o.Label == "C").IsCorrect.ShouldBeTrue();
    }
}
