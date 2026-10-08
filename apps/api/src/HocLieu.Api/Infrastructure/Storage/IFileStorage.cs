using HocLieu.Domain.Entities;

namespace HocLieu.Infrastructure.Storage;

/// <summary>Một "trang" preview. Kind = "image" (FE hiển thị &lt;img&gt;) hoặc "pdf" (FE hiển thị &lt;iframe&gt;).</summary>
public sealed record FilePage(int Number, string Url, string Kind);

/// <summary>
/// Trừu tượng lưu file (spec §11). Hai implementation:
/// – <see cref="CloudinaryFileStorage"/> khi có `Cloudinary__Url` (prod): URL ký, transform trang.
/// – <see cref="LocalDiskFileStorage"/> fallback (dev/sandbox, chưa có Cloudinary):
///   file nằm trên đĩa, API stream ra + token DataProtection cho URL preview.
/// </summary>
public interface IFileStorage
{
    /// <summary>true khi chạy Cloudinary (FE có thể dựa vào CDN).</summary>
    bool IsCloudinary { get; }

    /// <summary>Upload file gốc. `file.StoragePublicId` + `StorageResourceType` phải được đặt trước.</summary>
    Task UploadOriginalAsync(FileEntity file, Stream content, CancellationToken ct);

    /// <summary>Upload PDF preview (từ Gotenberg) cho file Office. Đặt `file.PreviewPublicId`.</summary>
    Task UploadPreviewPdfAsync(FileEntity file, Stream pdf, CancellationToken ct);

    /// <summary>URL preview cho các trang [from, from+count). Cloudinary: URL ký ảnh trang; Local: 1 URL token trỏ PDF/ảnh gốc.</summary>
    Task<IReadOnlyList<FilePage>> GetPreviewPagesAsync(FileEntity file, int from, int count, CancellationToken ct);

    /// <summary>
    /// URL tải về có hạn (Cloudinary: 302 sang URL ký; Local: null → endpoint stream trực tiếp).
    /// </summary>
    Task<string?> GetDownloadUrlAsync(FileEntity file, CancellationToken ct);

    /// <summary>
    /// URL hiển thị ảnh (ảnh nhúng trong câu hỏi quiz, spec §6.3.11).
    /// Cloudinary: URL ký (authenticated, không hết hạn — quyền kiểm ở endpoint API);
    /// Local: null → endpoint stream bytes trực tiếp.
    /// </summary>
    Task<string?> GetImageUrlAsync(FileEntity file, CancellationToken ct);

    /// <summary>Đọc bytes file gốc (worker dùng để gửi Gotenberg). Null nếu không đọc được.</summary>
    Task<byte[]?> GetOriginalBytesAsync(FileEntity file, CancellationToken ct);

    /// <summary>Xóa file gốc + preview (dùng cho CleanupWorker sau xóa mềm 30 ngày, spec §8.4).</summary>
    Task DeleteAsync(FileEntity file, CancellationToken ct);
}

/// <summary>MIME + nhóm định dạng cho upload/preview (spec §11.1, §11.2).</summary>
public static class FileTypes
{
    public static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
        ["mp4"] = "video/mp4",
    };

    public static bool IsOffice(string ext) => ext is "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx";
    public static bool IsImage(string ext) => ext is "jpg" or "jpeg" or "png" or "webp";
    public static bool IsPreviewable(string ext) => ext is "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "jpg" or "jpeg" or "png" or "webp";

    public static string MimeOf(string ext) => Mime.TryGetValue(ext, out var m) ? m : "application/octet-stream";

    /// <summary>§11.1: magic bytes đầu file phải khớp định dạng khai báo. Sai → 415.</summary>
    public static string? ValidateMagicBytes(byte[] head, string ext)
    {
        if (head.Length < 4)
            return "File rỗng hoặc quá nhỏ";

        switch (ext)
        {
            case "pdf":
                return head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46 // %PDF
                    ? null : "Nội dung file không phải PDF";
            case "docx":
            case "xlsx":
            case "pptx":
                if (head[0] != 0x50 || head[1] != 0x4B || head[2] != 0x03 || head[3] != 0x04) // PK\x03\x04
                    return "Nội dung file không khớp định dạng Office (.docx/.xlsx/.pptx)";
                return null; // [Content_Types].xml được kiểm ở tầng zip (FilesService)
            case "doc":
            case "xls":
            case "ppt":
                return head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 // OLE2
                    ? null : "Nội dung file không khớp định dạng Office cũ (.doc/.xls/.ppt)";
            case "jpg":
            case "jpeg":
                return head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF
                    ? null : "Nội dung file không phải ảnh JPEG";
            case "png":
                return head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                    ? null : "Nội dung file không phải ảnh PNG";
            case "webp":
                return head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
                       && head.Length >= 12 && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50
                    ? null : "Nội dung file không phải ảnh WebP";
            case "mp4":
                return head.Length >= 12 && head[4] == 0x66 && head[5] == 0x74 && head[6] == 0x79 && head[7] == 0x70
                    ? null : "Nội dung file không phải video MP4";
            default:
                return "Định dạng không được hỗ trợ";
        }
    }
}
