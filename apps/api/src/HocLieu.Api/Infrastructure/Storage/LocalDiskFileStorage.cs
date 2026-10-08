using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using HocLieu.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace HocLieu.Infrastructure.Storage;

/// <summary>
/// Lưu file trên đĩa (fallback khi chưa có Cloudinary — decisions.md M3).
/// Root: `Storage__LocalRoot` (mặc định `data/` tương đối content root).
/// Preview của Office (PDF từ Gotenberg) + PDF/ảnh gốc được stream qua
/// `/api/files/{id}/preview` kèm token DataProtection 10 phút (không lộ đường dẫn).
/// </summary>
public sealed class LocalDiskFileStorage(
    IOptionsMonitor<FileStorageOptions> options,
    IDataProtectionProvider dataProtection,
    IWebHostEnvironment env) : IFileStorage
{
    private const string PreviewPurpose = "hoclieu-file-preview";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    private string Root => Path.GetFullPath(
        Path.IsPathRooted(options.CurrentValue.LocalRoot)
            ? options.CurrentValue.LocalRoot
            : Path.Combine(env.ContentRootPath, options.CurrentValue.LocalRoot));

    public bool IsCloudinary => false;

    private string PathOf(string publicId)
    {
        // publicId dạng "files/{id}/{slug}" — chỉ cho phép chuỗi an toàn, chống path traversal
        var safe = publicId.Replace('\\', '/').TrimStart('/');
        if (safe.Contains(".."))
            throw new InvalidOperationException("publicId không hợp lệ");
        return Path.Combine(Root, safe);
    }

    public Task UploadOriginalAsync(FileEntity file, Stream content, CancellationToken ct)
    {
        var path = PathOf(file.StoragePublicId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return CopyAsync(content, path, ct);
    }

    public Task UploadPreviewPdfAsync(FileEntity file, Stream pdf, CancellationToken ct)
    {
        var path = Path.Combine(Root, "files", file.Id.ToString(), "preview.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        file.PreviewPublicId ??= "local-preview"; // cờ "đã có preview" (không dùng làm URL)
        return CopyAsync(pdf, path, ct);
    }

    private static async Task CopyAsync(Stream src, string path, CancellationToken ct)
    {
        // File.Create() mở sync-mode → handle đóng ngay, CopyToAsync sẽ ObjectDisposedException
        var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        try
        {
            await src.CopyToAsync(dst, ct);
        }
        finally
        {
            await dst.DisposeAsync();
        }
    }

    /// <summary>File preview hiện tại: PDF đã chuyển (nếu có) hoặc file gốc (pdf/ảnh). Trả null nếu chưa có gì.</summary>
    private (string Path, string Ext, string Kind)? PreviewFile(FileEntity file)
    {
        // Chế độ local: đĩa là nguồn sự thật — worker (Gotenberg) viết preview.pdf
        var localPreview = Path.Combine(Root, "files", file.Id.ToString(), "preview.pdf");
        if (File.Exists(localPreview))
            return (localPreview, "pdf", "pdf");
        if (file.Ext == "pdf")
            return (PathOf(file.StoragePublicId), "pdf", "pdf");
        if (FileTypes.IsImage(file.Ext))
            return (PathOf(file.StoragePublicId), file.Ext, "image");
        if (file.Ext == "mp4")
            return (PathOf(file.StoragePublicId), "mp4", "video");
        return null;
    }

    public Task<IReadOnlyList<FilePage>> GetPreviewPagesAsync(FileEntity file, int from, int count, CancellationToken ct)
    {
        var preview = PreviewFile(file);
        if (preview is null)
            return Task.FromResult<IReadOnlyList<FilePage>>([]);

        // Local không rasterize được từng trang → 1 "trang" duy nhất:
        // pdf → iframe; ảnh → img; video → video tag. (Cloudinary: pg_N ảnh, decisions.md M3)
        var (path, ext, kind) = preview.Value;
        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<FilePage>>([]);

        var url = $"/api/files/{file.Id}/preview?token={MakeToken(file.Id)}";
        return Task.FromResult<IReadOnlyList<FilePage>>([new FilePage(1, url, kind)]);
    }

    public Task<string?> GetDownloadUrlAsync(FileEntity file, CancellationToken ct)
        => Task.FromResult<string?>(null); // endpoint /download stream trực tiếp

    public Task<string?> GetImageUrlAsync(FileEntity file, CancellationToken ct)
        => Task.FromResult<string?>(null); // endpoint /image stream trực tiếp

    public async Task<byte[]?> GetOriginalBytesAsync(FileEntity file, CancellationToken ct)
    {
        var path = ResolveOriginalPath(file);
        return path is null ? null : await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(FileEntity file, CancellationToken ct)
    {
        try
        {
            DeleteIfExists(PathOf(file.StoragePublicId));
            DeleteIfExists(Path.Combine(Root, "files", file.Id.ToString(), "preview.pdf"));
        }
        catch (IOException)
        {
            // xóa đĩa là cleanup — lỗi không chặn xóa DB (CleanupWorker sẽ thử lại)
        }
        return Task.CompletedTask;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private string MakeToken(long fileId)
    {
        var protector = dataProtection.CreateProtector(PreviewPurpose);
        var payload = Encoding.UTF8.GetBytes($"{fileId}|{DateTime.UtcNow.Add(TokenLifetime):O}");
        // URL-safe (không có + / =) để token sống sót khi nhúng vào query string
        return EncodeBase64UrlSafe(protector.Protect(payload));
    }

    /// <summary>Endpoint /preview gọi: token hợp lệ + chưa hết hạn → true.</summary>
    public bool TryValidateToken(string? token, long fileId, out DateTime expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrEmpty(token))
            return false;
        try
        {
            var protector = dataProtection.CreateProtector(PreviewPurpose);
            var raw = protector.Unprotect(DecodeBase64UrlSafe(token));
            var parts = Encoding.UTF8.GetString(raw).Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out var fid) || fid != fileId)
                return false;
            expiresAt = DateTime.ParseExact(parts[1], "O", CultureInfo.InvariantCulture);
            return DateTime.UtcNow < expiresAt;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false; // token hỏng → từ chối (404), không được 500
        }
    }

    private static string EncodeBase64UrlSafe(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64UrlSafe(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        return Convert.FromBase64String(t);
    }

    /// <summary>Đường dẫn file cần stream cho endpoint /preview (null = từ chối).</summary>
    public (string Path, string Ext, string Kind)? ResolvePreviewPath(FileEntity file, string? token)
    {
        if (!TryValidateToken(token, file.Id, out _))
            return null;
        return PreviewFile(file);
    }

    /// <summary>Đường dẫn file gốc cho endpoint /download (local mode).</summary>
    public string? ResolveOriginalPath(FileEntity file)
        => File.Exists(PathOf(file.StoragePublicId)) ? PathOf(file.StoragePublicId) : null;
}

public sealed record FileStorageOptions
{
    public string LocalRoot { get; set; } = "data";
}
