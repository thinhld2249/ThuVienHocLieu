using System.Security.Cryptography;
using System.Text.Json;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Conversion;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Jobs;
using HocLieu.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Files;

/// <summary>
/// Upload + phân quyền xem/tải file (spec §11). Mọi kiểm tra quyền ở đây, FE chỉ hiển thị.
/// File gắn với tài liệu qua `document_files`: quyền xem file = quyền xem tài liệu chứa nó
/// (khách truy cập tài liệu không thấy được → 404, spec §4.3).
/// </summary>
public sealed class FilesService(
    AppDbContext db,
    IFileStorage storage,
    AppSettingsService settings,
    FileProcessingQueue queue,
    TimeProvider time,
    ILogger<FilesService> logger)
{
    public IFileStorage Storage => storage;

    /// <summary>§11.2 bước 1–4: kiểm tra đuôi/kích/magic/zip → ghi DB → upload storage → hàng đợi.</summary>
    public async Task<FileEntity> UploadAsync(IFormFile formFile, long ownerId, CancellationToken ct)
    {
        var originalName = System.IO.Path.GetFileName(formFile.FileName);
        var ext = System.IO.Path.GetExtension(originalName).TrimStart('.').ToLowerInvariant();

        var allowed = await settings.GetStringArrayAsync(SettingKeys.UploadAllowedExt, SettingKeys.DefaultAllowedExt, ct);
        if (!allowed.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new FileUploadException(415, "Định dạng file không được hỗ trợ.");

        var maxMb = await settings.GetIntAsync(SettingKeys.UploadMaxMb, SettingKeys.DefaultMaxMb, ct);
        var maxBytes = (long)maxMb * 1024 * 1024;
        if (formFile.Length <= 0 || formFile.Length > maxBytes)
            throw new FileUploadException(413, $"File phải có dung lượng từ 1 byte đến {maxMb} MB.");

        var bytes = new byte[formFile.Length];
        await using (var s = formFile.OpenReadStream())
            await s.ReadExactlyAsync(bytes, ct);

        var magicError = FileTypes.ValidateMagicBytes(bytes, ext);
        if (magicError is not null)
            throw new FileUploadException(415, magicError);

        if (ext is "docx" or "xlsx" or "pptx")
        {
            // §11.1: PK header đúng chưa đủ — phải là package OOXML có [Content_Types].xml
            using var ms = new MemoryStream(bytes, writable: false);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            if (!zip.Entries.Any(e => string.Equals(e.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase)))
                throw new FileUploadException(415, "File không phải tài liệu Office hợp lệ.");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var resourceType = FileTypes.IsImage(ext) || ext == "pdf" ? "image" : ext == "mp4" ? "video" : "raw";

        // 2 bước: lấy Id từ DB trước để publicId = files/{id}/{slug} (spec §11.2)
        var file = new FileEntity
        {
            OwnerId = ownerId,
            OriginalName = originalName,
            Ext = ext,
            Mime = FileTypes.MimeOf(ext),
            Bytes = bytes.Length,
            Sha256 = sha256,
            StoragePublicId = "pending",
            StorageResourceType = resourceType,
            ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = time.GetUtcNow(),
        };
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);

        var slug = Slugify.ToSlug(System.IO.Path.GetFileNameWithoutExtension(originalName));
        file.StoragePublicId = $"files/{file.Id}/{slug}";

        using var content = new MemoryStream(bytes, writable: false);
        try
        {
            await storage.UploadOriginalAsync(file, content, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload file {FileId} lên storage thất bại", file.Id);
            throw new FileUploadException(502, "Không lưu được file. Vui lòng thử lại.");
        }

        if (FileTypes.IsOffice(ext))
        {
            // Office: worker sẽ gửi Gotenberg (spec §11.2 bước 5)
            file.ProcessingStatus = ProcessingStatus.Pending;
            queue.Enqueue(file.Id);
        }
        else if (ext == "mp4")
        {
            file.ProcessingStatus = ProcessingStatus.NotApplicable; // không có preview trang
        }
        else
        {
            // Pdf/ảnh: preview là chính file → Ready ngay (decisions.md M3)
            file.ProcessingStatus = ProcessingStatus.Ready;
            file.PreviewPages = ext == "pdf" ? Math.Max(1, PdfPages.Count(bytes)) : 1;
        }

        await db.SaveChangesAsync(ct);
        return file;
    }

    /// <summary>File + tài liệu chứa nó (nếu có). File chưa gắn tài liệu → chỉ owner/Admin xem được.</summary>
    public async Task<(FileEntity File, Document? Doc)> GetWithDocumentAsync(long fileId, CancellationToken ct)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
            return (null!, null);

        var doc = await db.Documents.AsNoTracking()
            .Where(d => d.Files.Any(df => df.FileId == file.Id))
            .FirstOrDefaultAsync(ct);
        return (file, doc);
    }

    /// <summary>§4.3: tài liệu hiển thị với viewer (live + scope) ∨ owner ∨ Lead|Deputy của tổ ∨ Admin.</summary>
    public static bool DocumentVisibleTo(Document? doc, ViewerContext viewer, DateTimeOffset now)
    {
        if (doc is null)
            return false;
        if (doc.IsLive(now) && viewer.ScopeAllows(doc.Scope, doc.TeamId))
            return true;
        return viewer.IsAdmin
               || doc.OwnerId == viewer.UserId
               || (doc.TeamId is not null && viewer.LeadOrDeputyTeamIds.Contains(doc.TeamId.Value));
    }

    /// <summary>
    /// §11.3: URL preview từng trang. File chưa gắn tài liệu → chỉ owner/Admin.
    /// Trả null khi không có quyền (endpoint 404) hoặc chưa có preview (list rỗng).
    /// </summary>
    public async Task<FilePagesDto?> GetPagesAsync(long fileId, ViewerContext viewer, int from, int count, CancellationToken ct)
    {
        var (file, doc) = await GetWithDocumentAsync(fileId, ct);
        if (file is null)
            return null;

        if (doc is not null)
        {
            if (!DocumentVisibleTo(doc, viewer, time.GetUtcNow()))
                return null;
        }
        else if (file.OwnerId != viewer.UserId && !viewer.IsAdmin)
        {
            return null;
        }

        if (file.ProcessingStatus is ProcessingStatus.Pending or ProcessingStatus.Processing or ProcessingStatus.Failed
            && FileTypes.IsOffice(file.Ext))
            return new FilePagesDto(file.Id, []); // FE poll trạng thái file

        var pages = await storage.GetPreviewPagesAsync(file, from, count, ct);
        return new FilePagesDto(file.Id, pages.Select(p => new FilePageDto(p.Number, p.Url, p.Kind)).ToList());
    }

    /// <summary>
    /// §11.3: quyền tải = tài liệu hiển thị với viewer ∧ (khách → allow_guest_download).
    /// §6.5: file đề in của quiz (print_file_id) = quiz hiển thị với viewer (khách được, không gate khách tải).
    /// Trả null khi không có quyền; nếu storage trả URL → endpoint 302, nếu null → endpoint stream (local).
    /// </summary>
    public async Task<(FileEntity File, Document? Doc, string? Url)?> GetDownloadAsync(long fileId, ViewerContext viewer, CancellationToken ct)
    {
        var (file, doc) = await GetWithDocumentAsync(fileId, ct);
        if (file is null)
            return null;

        var now = time.GetUtcNow();
        if (doc is not null)
        {
            if (!DocumentVisibleTo(doc, viewer, now))
                return null;
            if (viewer.UserId is null && !doc.AllowGuestDownload)
                return null;

            var url = await storage.GetDownloadUrlAsync(file, ct);

            // §11.3: tăng download_count
            await db.Documents
                .Where(d => d.Id == doc.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DownloadCount, d => d.DownloadCount + 1));

            return (file, doc, url);
        }

        if (file.QuizId is not null)
        {
            var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == file.QuizId, ct);
            if (quiz is null || !Visibility.QuizVisibleTo(quiz, viewer, now))
                return null;
            return (file, null, await storage.GetDownloadUrlAsync(file, ct));
        }

        return null;
    }
}

/// <summary>Lỗi upload hợp lệ (status = 415/413) — endpoint chuyển ProblemDetails.</summary>
public sealed class FileUploadException(int status, string title) : Exception(title)
{
    public int Status { get; } = status;
}
