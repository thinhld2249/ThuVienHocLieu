namespace HocLieu.QuizImport;

/// <summary>Lỗi parse import (file không hợp lệ, quá giới hạn) — tầng API chuyển thành 415/413.</summary>
public sealed class ImportException : Exception
{
    public ImportException(string message, int httpStatus = 415) : base(message)
    {
        HttpStatus = httpStatus;
    }

    /// <summary>415 = sai định dạng, 413 = quá dung lượng.</summary>
    public int HttpStatus { get; }
}
