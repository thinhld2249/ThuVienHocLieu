using ClosedXML.Excel;
using HocLieu.QuizImport.Tests;

// Sinh file mẫu (commit vào repo) cho GV tải về:
//   apps/web/public/templates/mau-bai-tap.docx      — theo đúng ví dụ spec §6.2
//   apps/web/public/templates/mau-bai-tap.xlsx      — cột theo spec §6.4
//   apps/web/public/templates/mau-danh-sach-hs.xlsx — cột theo spec §7
// Chạy lại khi cần: dotnet run --project apps/api/tools/TemplateGen

var templatesDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "web", "public", "templates"));
Directory.CreateDirectory(templatesDir);

GenDocxTemplate(Path.Combine(templatesDir, "mau-bai-tap.docx"));
GenXlsxQuizTemplate(Path.Combine(templatesDir, "mau-bai-tap.xlsx"));
GenXlsxHsTemplate(Path.Combine(templatesDir, "mau-danh-sach-hs.xlsx"));

Console.WriteLine($"Đã sinh 3 file mẫu vào {templatesDir}");

static void GenDocxTemplate(string path)
{
    using var b = DocxBuilder.Create();
    b.Para("BÀI TẬP CUỐI TUẦN 5 – TOÁN 5");
    b.Para("PHẦN I. TRẮC NGHIỆM");
    b.Para("Câu 1: Số thập phân gồm 5 đơn vị, 7 phần mười viết là:");
    b.Para("A. 5,07        *B. 5,7        C. 57        D. 0,57");
    b.Para("Câu 2. Phân số nào bằng 3/4?");
    b.Para("A. 6/8");
    b.Para("B. 4/3");
    b.Para("C. 3/8");
    b.Para("D. 9/16");
    b.Para("Đáp án: A");
    b.Para("Giải thích: Nhân cả tử và mẫu của 3/4 với 2 được 6/8.");
    b.Para("Câu 3: Những số nào lớn hơn 2,5?");
    b.Para("*A. 2,51");
    b.Para("B. 2,05");
    b.Para("*C. 3");
    b.Para("D. 2,499");
    b.Para("Câu 4: 0,5 = 1/2.");
    b.Para("*Đúng");
    b.Para("Sai");
    b.Para("PHẦN II. ĐỌC HIỂU");
    b.Para("Đọc đoạn văn sau và trả lời câu 5 và 6:");
    b.Para("   Mùa thu, bầu trời như cao hơn, không khí như trong lành hơn.");
    b.Para("Câu 5: Đoạn văn tả cảnh mùa nào?");
    b.Para("A. Mùa xuân    B. Mùa hạ    C. Mùa thu    D. Mùa đông");
    b.Para("Câu 6: Từ nào là danh từ chung?");
    b.Para("A. Hà Nội      B. sông      C. Hồng       D. Việt Nam");
    b.Para("ĐÁP ÁN");
    b.Para("5.C  6.B");
    File.WriteAllBytes(path, b.ToBytes());
}

static void GenXlsxQuizTemplate(string path)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Bai tap");

    string[] headers = { "STT", "Câu hỏi", "A", "B", "C", "D", "Đáp án", "Giải thích", "Nhóm", "Đoạn văn", "Điểm" };
    for (var i = 0; i < headers.Length; i++)
        ws.Cell(1, i + 1).Value = headers[i];

    // Row 1 — 1 đáp án. Phương án ghi dạng TEXT để giữ dấu phẩy thập phân Việt Nam
    // ("5,7" không bị Excel hiểu thành số rồi trả về "5.7").
    ws.Cell(2, 1).Value = 1;
    ws.Cell(2, 2).Value = "Số thập phân gồm 5 đơn vị, 7 phần mười viết là:";
    ws.Cell(2, 3).Value = "5,07";
    ws.Cell(2, 4).Value = "5,7";
    ws.Cell(2, 5).Value = "57";
    ws.Cell(2, 6).Value = "0,57";
    ws.Cell(2, 7).Value = "B";

    // Row 2 — dòng "Đáp án:" + giải thích
    ws.Cell(3, 1).Value = 2;
    ws.Cell(3, 2).Value = "Phân số nào bằng 3/4?";
    ws.Cell(3, 3).Value = "6/8";
    ws.Cell(3, 4).Value = "4/3";
    ws.Cell(3, 5).Value = "3/8";
    ws.Cell(3, 6).Value = "9/16";
    ws.Cell(3, 7).Value = "A";
    ws.Cell(3, 8).Value = "Nhân cả tử và mẫu của 3/4 với 2 được 6/8.";

    // Row 3 — chọn nhiều đáp án
    ws.Cell(4, 1).Value = 3;
    ws.Cell(4, 2).Value = "Những số nào lớn hơn 2,5?";
    ws.Cell(4, 3).Value = "2,51";
    ws.Cell(4, 4).Value = "2,05";
    ws.Cell(4, 5).Value = "3";
    ws.Cell(4, 6).Value = "2,499";
    ws.Cell(4, 7).Value = "A, C";

    // Row 4 — câu Đúng/Sai
    ws.Cell(5, 1).Value = 4;
    ws.Cell(5, 2).Value = "0,5 = 1/2.";
    ws.Cell(5, 3).Value = "Đúng";
    ws.Cell(5, 4).Value = "Sai";
    ws.Cell(5, 7).Value = "Đúng";

    // Row 5 — nhóm + đoạn văn
    ws.Cell(6, 1).Value = 5;
    ws.Cell(6, 2).Value = "Đoạn văn tả cảnh mùa nào?";
    ws.Cell(6, 3).Value = "Mùa xuân";
    ws.Cell(6, 4).Value = "Mùa hạ";
    ws.Cell(6, 5).Value = "Mùa thu";
    ws.Cell(6, 6).Value = "Mùa đông";
    ws.Cell(6, 7).Value = "C";
    ws.Cell(6, 9).Value = "PHẦN II. ĐỌC HIỂU";
    ws.Cell(6, 10).Value = "Mùa thu, bầu trời như cao hơn, không khí như trong lành hơn.";

    // Row 6 — cùng nhóm (cột Nhóm để trống)
    ws.Cell(7, 1).Value = 6;
    ws.Cell(7, 2).Value = "Từ nào là danh từ chung?";
    ws.Cell(7, 3).Value = "Hà Nội";
    ws.Cell(7, 4).Value = "sông";
    ws.Cell(7, 5).Value = "Hồng";
    ws.Cell(7, 6).Value = "Việt Nam";
    ws.Cell(7, 7).Value = "B";

    ws.Column(2).Width = 50;
    for (var c = 3; c <= 6; c++)
        ws.Column(c).Width = 16;
    ws.Column(8).Width = 40;

    wb.SaveAs(path);
}

static void GenXlsxHsTemplate(string path)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Danh sach HS");

    string[] headers = { "STT", "Họ và tên", "Ngày sinh", "Giới tính", "Mã HS" };
    for (var i = 0; i < headers.Length; i++)
        ws.Cell(1, i + 1).Value = headers[i];

    ws.Column(2).Width = 32;
    ws.Column(3).Width = 14;
    ws.Column(4).Width = 12;
    ws.Column(5).Width = 12;

    var guide = wb.Worksheets.Add("Huong dan");
    guide.Cell(1, 1).Value = "Nhập từng học sinh một dòng. Cột \"Họ và tên\" bắt buộc.";
    guide.Cell(2, 1).Value = "Ngày sinh định dạng dd/MM/yyyy. Trùng họ tên + ngày sinh với học sinh đã có sẽ bị bỏ qua.";
    guide.Cell(3, 1).Value = "Lưu file dưới định dạng .xlsx (Excel 2007 trở lên).";
    guide.Column(1).Width = 100;

    wb.SaveAs(path);
}
