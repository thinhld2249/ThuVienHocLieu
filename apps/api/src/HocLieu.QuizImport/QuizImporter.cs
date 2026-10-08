using HocLieu.QuizImport.Docx;
using HocLieu.QuizImport.Model;
using HocLieu.QuizImport.Xlsx;

namespace HocLieu.QuizImport;

/// <summary>
/// Facade nhập quiz từ file (spec §6.3.1): kiểm tra giới hạn,
/// docx/xlsx → <see cref="QuizDraft"/> + cảnh báo.
/// </summary>
public static class QuizImporter
{
    public const int MaxFileSizeMb = 20;
    public const int MaxQuestions = 300;

    public static QuizDraft ParseDocx(Stream stream, long? length = null)
    {
        if (length is > MaxFileSizeMb * 1024L * 1024L)
            throw new ImportException($"File quá lớn (tối đa {MaxFileSizeMb} MB).", 413);

        var draft = ParseDocxInternal(stream);
        CheckQuestionCount(draft);
        return draft;
    }

    public static QuizDraft ParseXlsx(Stream stream, long? length = null)
    {
        if (length is > MaxFileSizeMb * 1024L * 1024L)
            throw new ImportException($"File quá lớn (tối đa {MaxFileSizeMb} MB).", 413);

        var draft = ParseXlsxInternal(stream);
        CheckQuestionCount(draft);
        return draft;
    }

    private static QuizDraft ParseDocxInternal(Stream stream)
    {
        try
        {
            var content = DocxReader.Read(stream);
            if (content.Lines.Count == 0)
                throw new ImportException("File không có nội dung nào để tạo bài tập.", 415);
            return QuizStateMachine.Build(content.Lines, content.Images);
        }
        catch (ImportException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ImportException(
                "File không phải .docx hợp lệ. Mở file bằng Word → Lưu thành → Word Document (.docx) rồi tải lên lại.", 415);
        }
    }

    private static QuizDraft ParseXlsxInternal(Stream stream)
    {
        try
        {
            return XlsxQuizReader.Read(stream);
        }
        catch (ImportException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ImportException(
                "File không phải .xlsx hợp lệ. Kiểm tra file mẫu rồi thử lại.", 415);
        }
    }

    private static void CheckQuestionCount(QuizDraft draft)
    {
        if (draft.Questions.Count > MaxQuestions)
            throw new ImportException($"Bài tập có {draft.Questions.Count} câu (tối đa {MaxQuestions}).", 413);
    }
}
