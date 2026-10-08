using DocumentFormat.OpenXml.Math;
using HocLieu.QuizImport;
using HocLieu.QuizImport.Model;
using Shouldly;
using Xunit;

namespace HocLieu.QuizImport.Tests;

/// <summary>§6.3 test 13 — công thức OMML: phân số, chỉ số trên/dưới, cấu trúc phức tạp.</summary>
public class OmmlTests
{
    [Fact]
    public void Fraction_is_linearized_as_slash()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: ", Mathx.Frac("3", "4"), " bằng số thập phân nào?")
         .Para("A. 0,34")
         .Para("*B. 0,75")
         .Para("C. 7,5")
         .Para("D. 34");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.ContentHtml.ShouldContain("3/4");
        q.Options.Single(o => o.Label == "B").IsCorrect.ShouldBeTrue();
        draft.Warnings.ShouldNotContain(w => w.Code == WarningCodes.EquationSimplified);
    }

    [Fact]
    public void Superscript_is_linearized_with_sup_tag()
    {
        using var b = DocxBuilder.Create();
        b.Para("Câu 1: ", Mathx.Sup("x", "2"), " = 4. x bằng?")
         .Para("A. 2")
         .Para("*B. -2")
         .Para("C. 16");

        var draft = QuizImporter.ParseDocx(b.ToStream());
        var q = draft.Questions.Single();

        q.ContentHtml.ShouldContain("x<sup>2</sup>");
        draft.Warnings.ShouldNotContain(w => w.Code == WarningCodes.EquationSimplified);
    }

    [Fact]
    public void Nested_math_structure_produces_simplified_warning()
    {
        // oMath lồng trực tiếp trong oMath — cấu trúc không tuyến tính hóa được → EQUATION_SIMPLIFIED
        using var b = DocxBuilder.Create();
        var nested = new OfficeMath(new OfficeMath(new Run(new Text("y"))));
        b.Para("Câu 1: ", nested, " = ?")
         .Para("A. 1")
         .Para("*B. 2")
         .Para("C. 3");

        var draft = QuizImporter.ParseDocx(b.ToStream());

        draft.Warnings.ShouldContain(w => w.Code == WarningCodes.EquationSimplified);
        var q = draft.Questions.Single();
        q.ContentHtml.ShouldContain("y");
    }
}
