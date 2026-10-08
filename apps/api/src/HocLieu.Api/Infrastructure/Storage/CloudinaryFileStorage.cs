using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using HocLieu.Domain.Entities;

namespace HocLieu.Infrastructure.Storage;

/// <summary>
/// Cloudinary (spec §11.2–11.3). Asset gốc upload với <c>access_mode=authenticated</c>,
/// public_id <c>{root}/files/{fileId}/{slug}</c>; preview Office (Gotenberg) upload là
/// image PDF tại <c>{root}/files/{fileId}/preview</c>.
/// Mọi URL trả về FE đều được ký (delivery <c>type=authenticated</c>) — FE không bao giờ biết secret.
/// API dùng được kiểm chứng với CloudinaryDotNet 1.29.3 (UploadAsync/ImageUploadParams/RawUploadParams/
/// VideoUploadParams + FileDescription, Url.Signed, DownloadPrivate, DestroyAsync).
/// </summary>
public sealed class CloudinaryFileStorage(Cloudinary cloudinary, string root, string cloudName) : IFileStorage
{
    private const int UrlSeconds = 600; // 10 phút (spec §11.3)

    public bool IsCloudinary => true;

    public async Task UploadOriginalAsync(FileEntity file, Stream content, CancellationToken ct)
    {
        var publicId = FullId(file.StoragePublicId);
        var fileDesc = new FileDescription(file.OriginalName, content);

        switch (file.StorageResourceType)
        {
            case "video":
                await cloudinary.UploadAsync(new VideoUploadParams
                {
                    File = fileDesc,
                    PublicId = publicId,
                    AccessMode = "authenticated",
                }, ct).ConfigureAwait(false);
                break;
            case "raw":
                await cloudinary.UploadAsync(new RawUploadParams
                {
                    File = fileDesc,
                    PublicId = publicId,
                    AccessMode = "authenticated",
                }, "auto", ct).ConfigureAwait(false);
                break;
            default: // image (pdf, jpg, png, webp)
                await cloudinary.UploadAsync(new ImageUploadParams
                {
                    File = fileDesc,
                    PublicId = publicId,
                    AccessMode = "authenticated",
                }, ct).ConfigureAwait(false);
                break;
        }
    }

    public async Task UploadPreviewPdfAsync(FileEntity file, Stream pdf, CancellationToken ct)
    {
        // files/{id}/{slug} → files/{id}/preview
        var id = file.StoragePublicId;
        var idx = id.LastIndexOf('/');
        var previewId = idx > 0 ? id[..idx] + "/preview" : id + "/preview";

        var result = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(previewId + ".pdf", pdf),
            PublicId = FullId(previewId),
            AccessMode = "authenticated",
        }, ct).ConfigureAwait(false);

        file.PreviewPublicId = string.IsNullOrEmpty(result.PublicId) ? previewId : StripRoot(result.PublicId);
    }

    public Task<IReadOnlyList<FilePage>> GetPreviewPagesAsync(FileEntity file, int from, int count, CancellationToken ct)
    {
        var pages = new List<FilePage>();
        if (count <= 0)
            return Task.FromResult<IReadOnlyList<FilePage>>(pages);

        if (file.StorageResourceType == "video")
        {
            pages.Add(new FilePage(1, Sign(file.StoragePublicId, "video", "w_1100"), "video"));
        }
        else if (FileTypes.IsImage(file.Ext))
        {
            // Ảnh đơn: 1 "trang"
            pages.Add(new FilePage(1, Sign(file.StoragePublicId, "image", "w_1100,c_limit,f_auto,q_auto"), "image"));
        }
        else
        {
            // PDF gốc hoặc PDF preview (Office qua Gotenberg): render từng trang
            var publicId = file.PreviewPublicId ?? file.StoragePublicId;
            var total = file.PreviewPages ?? 1;
            var start = Math.Max(1, from);
            var end = Math.Min(total, start + count - 1);
            for (var n = start; n <= end; n++)
                pages.Add(new FilePage(n, Sign(publicId, "image", $"pg_{n},w_1100,c_limit,f_auto,q_auto"), "image"));
        }

        return Task.FromResult<IReadOnlyList<FilePage>>(pages);
    }

    public Task<string?> GetDownloadUrlAsync(FileEntity file, CancellationToken ct)
    {
        var url = cloudinary.DownloadPrivate(
            publicId: FullId(file.StoragePublicId),
            attachment: true,
            format: "",
            type: "authenticated",
            expiresAt: TimeProvider.System.GetUtcNow().AddSeconds(UrlSeconds).ToUnixTimeSeconds(),
            resourceType: file.StorageResourceType, // raw | image | video
            transformation: null,
            targetFilename: file.OriginalName);
        return Task.FromResult<string?>(url);
    }

    public Task<string?> GetImageUrlAsync(FileEntity file, CancellationToken ct)
    {
        // Ảnh trong đề: cùng transform ảnh đơn của preview, không có expires (type=authenticated
        // ký theo publicId + api_secret — quyền xem vẫn do endpoint /image kiểm tra)
        return Task.FromResult<string?>(Sign(file.StoragePublicId, "image", "w_1100,c_limit,f_auto,q_auto"));
    }

    public async Task<byte[]?> GetOriginalBytesAsync(FileEntity file, CancellationToken ct)
    {
        var url = await GetDownloadUrlAsync(file, ct);
        if (url is null)
            return null;
        return await DownloadHttp.GetByteArrayAsync(url, ct);
    }

    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task DeleteAsync(FileEntity file, CancellationToken ct)
    {
        // Best-effort: CleanupWorker gọi đây, lỗi Cloudinary không được chặn luồng xóa DB.
        try
        {
            await cloudinary.DestroyAsync(new DeletionParams(FullId(file.StoragePublicId))
            {
                ResourceType = MapResourceType(file.StorageResourceType),
                Type = "authenticated",
            }).ConfigureAwait(false);
        }
        catch { /* log ở tầng caller */ }

        if (!string.IsNullOrEmpty(file.PreviewPublicId))
        {
            try
            {
                await cloudinary.DestroyAsync(new DeletionParams(FullId(file.PreviewPublicId))
                {
                    ResourceType = CloudinaryDotNet.Actions.ResourceType.Image,
                    Type = "authenticated",
                }).ConfigureAwait(false);
            }
            catch { /* log ở tầng caller */ }
        }
    }

    /// <summary>`files.StorageResourceType` (string) → enum của CloudinaryDotNet.</summary>
    private static CloudinaryDotNet.Actions.ResourceType MapResourceType(string value) => value switch
    {
        "raw" => CloudinaryDotNet.Actions.ResourceType.Raw,
        "video" => CloudinaryDotNet.Actions.ResourceType.Video,
        _ => CloudinaryDotNet.Actions.ResourceType.Image,
    };

    private string FullId(string publicId) => string.IsNullOrEmpty(root) ? publicId : $"{root}/{publicId}";

    private string StripRoot(string publicId) =>
        string.IsNullOrEmpty(root) ? publicId : (publicId.StartsWith($"{root}/", StringComparison.Ordinal) ? publicId[(root.Length + 1)..] : publicId);

    /// <summary>URL delivery đã ký cho asset authenticated:
    /// https://res.cloudinary.com/{cloud}/{resourceType}/authenticated/s--…--/{transform}/{publicId}</summary>
    private string Sign(string publicId, string resourceType, string transform)
    {
        return new Url(cloudName, cloudinary.Api)
            .Secure(true)
            .ResourceType(resourceType)
            .Type("authenticated")
            .Transform(new Transformation(new[] { transform }))
            .Signed(true)
            .Source(FullId(publicId))
            .BuildUrl();
    }
}
