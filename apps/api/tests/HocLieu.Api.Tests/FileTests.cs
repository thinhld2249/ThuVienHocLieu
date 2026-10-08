using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Auth;
using HocLieu.Infrastructure.Conversion;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M3 (spec §11): upload + preview + phân quyền xem/tải file.
/// Storage = LocalDiskFileStorage (không có Cloudinary trong test, decisions.md M3):
/// pdf/ảnh Ready ngay; docx đi hàng đợi + worker (Gotenberg thay bằng stub).
/// Docker không khả dụng → skip (spec §16).
/// </summary>
public class FileTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public FileTests(TestApp app) => _app = app;

    private void EnsureFactory() => _factory ??= _app.CreateFactory();

    /// <summary>Client KHÔNG cookie container (cookie cầm tay) — không lẫn phiên giữa các user.</summary>
    private System.Net.Http.HttpClient Client
    {
        get
        {
            if (!_app.DockerAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");
            EnsureFactory();
            _anonClient ??= new Lazy<System.Net.Http.HttpClient>(() =>
                _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
            return _anonClient.Value;
        }
    }

    // ===== Dữ liệu mẫu =====

    /// <summary>PDF 1 trang tối thiểu (đủ magic %PDF + 1 /Type /Page cho PdfPages.Count).</summary>
    private static readonly byte[] SamplePdf =
    """
    %PDF-1.4
    1 0 obj
    << /Type /Catalog /Pages 2 0 R >>
    endobj
    2 0 obj
    << /Type /Pages /Kids [3 0 R] /Count 1 >>
    endobj
    3 0 obj
    << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>
    endobj
    trailer
    << /Size 4 /Root 1 0 R >>
    %%EOF
    """u8.ToArray();

    private static byte[] MakeDocxBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("[Content_Types].xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        }
        return ms.ToArray();
    }

    /// <summary>Gotenberg giả: mọi file Office → PDF mẫu (test không chạy container Gotenberg).</summary>
    private sealed class StubPdfConverter : IDocumentConverter
    {
        public Task<byte[]> ConvertToPdfAsync(string fileName, byte[] content, CancellationToken ct)
            => Task.FromResult(SamplePdf);
    }

    // ===== Helpers =====

    private sealed record Actor(long Id, string Email, string Cookie);

    private static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Actor> CreateTeacherAsync(string email, string name)
    {
        var tag = NewTag();
        long id;
        await using (var db = _app.CreateDb())
        {
            var user = new User
            {
                Email = email,
                GoogleSub = $"sub-{tag}",
                FullName = name,
                SystemRole = SystemRole.Teacher,
                Status = UserStatus.Active,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        }
        _app.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        EnsureFactory();
        _factory!.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-" + tag));
        cookie.ShouldNotBeNullOrEmpty();
        return new Actor(id, email, cookie!);
    }

    private static Task<HttpResponseMessage> PostLoginAsync(System.Net.Http.HttpClient client, string token)
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string?> { ["idToken"] = token }),
            Encoding.UTF8,
            "application/json");
        body.Headers.Add("X-Requested-With", "hoclieu");
        return client.PostAsync("/api/auth/google", body);
    }

    private static string? SessionCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;
        foreach (var raw in values)
        {
            var cookie = raw.Split(';', 2)[0];
            if (cookie.StartsWith("hl_session=", StringComparison.OrdinalIgnoreCase))
                return cookie["hl_session=".Length..];
        }
        return null;
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string? cookie = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (cookie is not null)
            req.Headers.Add("Cookie", $"hl_session={cookie}");
        return req;
    }

    private sealed record UploadResult(HttpStatusCode Status, JsonElement Json);

    private static async Task<UploadResult> UploadAsync(
        System.Net.Http.HttpClient client, string cookie, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", fileName);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/files") { Content = form };
        req.Headers.Add("Cookie", $"hl_session={cookie}");
        req.Headers.Add("X-Requested-With", "hoclieu");
        var response = await client.SendAsync(req);
        var json = (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        return new UploadResult(response.StatusCode, json);
    }

    /// <summary>Gắn file vào tài liệu mới (bỏ qua endpoint M3-documents — test tầng file).</summary>
    private async Task<long> CreateDocWithFileAsync(
        long ownerId, long fileId, ContentScope scope, PublishMode mode, bool allowGuestDownload)
    {
        await using var db = _app.CreateDb();
        var section = await db.Sections.FirstAsync(s => s.Slug == "ke-hoach-bai-day");
        var year = await db.SchoolYears.FirstAsync(y => y.IsCurrent);
        var doc = new Document
        {
            Title = "Tài liệu kiểm tra " + NewTag(),
            Slug = "tlkt-" + NewTag(),
            SectionId = section.Id,
            GradeId = 5,
            SchoolYearId = year.Id,
            OwnerId = ownerId,
            Scope = scope,
            PublishMode = mode,
            AllowGuestDownload = allowGuestDownload,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        db.DocumentFiles.Add(new DocumentFile { DocumentId = doc.Id, FileId = fileId, Sort = 1 });
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;

    // ===== Upload =====

    [Fact]
    public async Task UploadPdf_ReturnsReady_AndStreamsPage()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV File");

        var result = await UploadAsync(Client, teacher.Cookie, "de-thi.pdf", SamplePdf);
        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Json.GetProperty("id").GetInt64().ShouldBeGreaterThan(0);
        result.Json.GetProperty("processingStatus").GetString().ShouldBe("Ready");
        result.Json.GetProperty("previewPages").GetInt32().ShouldBe(1);
        var fileId = result.Json.GetProperty("id").GetInt64();

        // Preview: 1 "trang" kind=pdf, URL token 10 phút (chế độ local)
        var pagesResp = await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/pages", teacher.Cookie));
        pagesResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pages = await JsonAsync(pagesResp);
        pages.GetProperty("pages").GetArrayLength().ShouldBe(1);
        var page = pages.GetProperty("pages")[0];
        page.GetProperty("kind").GetString().ShouldBe("pdf");
        var url = page.GetProperty("url").GetString()!;
        url.ShouldStartWith($"/api/files/{fileId}/preview?token=");

        // Token hợp lệ → stream đúng byte PDF
        var previewResp = await Client.GetAsync(url);
        previewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        previewResp.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await previewResp.Content.ReadAsByteArrayAsync();
        bytes.ShouldBe(SamplePdf);

        // Token sai → 404
        (await Client.GetAsync($"/api/files/{fileId}/preview?token=sai")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadDocx_Enqueued_WorkerMakesReady_WithStubConverter()
    {
        // Factory riêng: stub IDocumentConverter + storage root riêng
        if (!_app.DockerAvailable)
            throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");
        var storageRoot = Path.Combine(Path.GetTempPath(), "hoclieu-tests-docx-" + NewTag());
        var factory = _app.CreateFactory(new Dictionary<string, string?>
        {
            ["Storage:LocalRoot"] = storageRoot,
        });
        factory.ServiceOverrides = s => s.AddSingleton<IDocumentConverter>(new StubPdfConverter());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var tag = NewTag();
        long userId;
        await using (var db = _app.CreateDb())
        {
            var user = new User
            {
                Email = $"t-{tag}@gmail.com",
                GoogleSub = $"sub-{tag}",
                FullName = "GV Docx",
                SystemRole = SystemRole.Teacher,
                Status = UserStatus.Active,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }
        _app.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV Docx", null, null);
        factory.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV Docx", null, null);
        var cookie = SessionCookieValue(await PostLoginAsync(client, "tok-" + tag))!;

        var result = await UploadAsync(client, cookie, "ke-hoach.docx", MakeDocxBytes());
        result.Status.ShouldBe(HttpStatusCode.OK);
        result.Json.GetProperty("processingStatus").GetString().ShouldBeOneOf("Pending", "Processing");
        var fileId = result.Json.GetProperty("id").GetInt64();

        // Worker: Pending → (stub converter) → Ready. Poll tối đa 15s.
        var status = "";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var r = await client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/files/{fileId}", cookie));
            r.StatusCode.ShouldBe(HttpStatusCode.OK);
            status = (await JsonAsync(r)).GetProperty("processingStatus").GetString()!;
            if (status is "Ready" or "Failed")
                break;
            await Task.Delay(250);
        }
        status.ShouldBe("Ready");

        var r2 = await client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/files/{fileId}", cookie));
        var final = await JsonAsync(r2);
        final.GetProperty("previewPages").GetInt32().ShouldBe(1);

        // Preview của docx = PDF (kind "pdf"), nội dung = stub PDF
        var pagesResp = await client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/pages", cookie));
        var pages = await JsonAsync(pagesResp);
        pages.GetProperty("pages")[0].GetProperty("kind").GetString().ShouldBe("pdf");
        var previewResp = await client.GetAsync(pages.GetProperty("pages")[0].GetProperty("url").GetString()!);
        previewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await previewResp.Content.ReadAsByteArrayAsync()).ShouldBe(SamplePdf);

        try { Directory.Delete(storageRoot, recursive: true); } catch (IOException) { /* ignore */ }
    }

    [Fact]
    public async Task Upload_WrongMagicBytes_Returns415()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV 415");
        var result = await UploadAsync(Client, teacher.Cookie, "gioi-thieu.pdf", Encoding.UTF8.GetBytes("chỉ là văn bản"));
        result.Status.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        result.Json.GetProperty("code").GetString().ShouldBe(ErrorCodes.FileInvalid);
    }

    [Fact]
    public async Task Upload_WrongMagicForDocx_Returns415()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV DocxFake");
        // PK\x03\x04 đúng nhưng không phải zip OOXML (thiếu [Content_Types].xml)
        var fakeZip = MakeZipWithoutContentTypes();
        var result = await UploadAsync(Client, teacher.Cookie, "bai-giang.docx", fakeZip);
        result.Status.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    private static byte[] MakeZipWithoutContentTypes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("other.xml");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write("<x/>");
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Upload_Oversize_Returns413()
    {
        if (!_app.DockerAvailable)
            throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");

        // max_mb = 0 → mọi file đều vượt hạn mức. Factory riêng (cache setting trống).
        // Seed đã có sẵn row upload.max_mb → upsert thay vì insert, restore giá trị cũ.
        var hadOriginal = false;
        var originalValue = "";
        await using (var db = _app.CreateDb())
        {
            var existing = await db.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == SettingKeys.UploadMaxMb);
            hadOriginal = existing is not null;
            originalValue = existing?.Value ?? "";
            if (existing is not null)
            {
                db.AppSettings.Update(existing);
                existing.Value = "0";
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingKeys.UploadMaxMb,
                    Value = "0",
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            var factory = _app.CreateFactory(new Dictionary<string, string?>
            {
                ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "hoclieu-tests-413-" + NewTag()),
            });
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var tag = NewTag();
            await using (var db = _app.CreateDb())
            {
                var user = new User
                {
                    Email = $"t-{tag}@gmail.com",
                    GoogleSub = $"sub-{tag}",
                    FullName = "GV 413",
                    SystemRole = SystemRole.Teacher,
                    Status = UserStatus.Active,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            _app.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV 413", null, null);
            factory.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV 413", null, null);
            var cookie = SessionCookieValue(await PostLoginAsync(client, "tok-" + tag))!;

            var result = await UploadAsync(client, cookie, "lon.pdf", SamplePdf);
            result.Status.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        }
        finally
        {
            await using var db = _app.CreateDb();
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.UploadMaxMb);
            if (row is not null)
            {
                if (hadOriginal)
                {
                    row.Value = originalValue;
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    db.AppSettings.Remove(row);
                }
                await db.SaveChangesAsync();
            }
        }
    }

    // ===== Phân quyền xem/tải (quyền theo tài liệu chứa file, spec §11.3) =====

    [Fact]
    public async Task HiddenDoc_GuestGets404_OwnerSeesPages()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Hidden");
        var result = await UploadAsync(Client, teacher.Cookie, "bat-tai.pdf", SamplePdf);
        result.Status.ShouldBe(HttpStatusCode.OK);
        var fileId = result.Json.GetProperty("id").GetInt64();
        await CreateDocWithFileAsync(teacher.Id, fileId, ContentScope.Public, PublishMode.Hidden, false);

        // Khách: tài liệu ẩn → 404 (không phải 403, spec §4.3)
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/pages"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/download"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Owner vẫn thấy
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/pages", teacher.Cookie))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VisibleDoc_Download_GuestBlockedUntilAllowed_OwnerCounted()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Download");
        var result = await UploadAsync(Client, teacher.Cookie, "ke-hoach.pdf", SamplePdf);
        result.Status.ShouldBe(HttpStatusCode.OK);
        var fileId = result.Json.GetProperty("id").GetInt64();
        var docId = await CreateDocWithFileAsync(teacher.Id, fileId, ContentScope.Public, PublishMode.Visible, false);

        // Khách bị chặn khi tài liệu không cho phép
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/download"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Owner tải được (chế độ local: stream trực tiếp)
        var ownerResp = await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/download", teacher.Cookie));
        ownerResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        ownerResp.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await ownerResp.Content.ReadAsByteArrayAsync()).ShouldBe(SamplePdf);

        // Bật allow_guest_download → khách tải được
        await using (var db = _app.CreateDb())
        {
            var doc = await db.Documents.SingleAsync(d => d.Id == docId);
            doc.AllowGuestDownload = true;
            await db.SaveChangesAsync();
        }
        var guestResp = await Client.SendAsync(Req(HttpMethod.Get, $"/api/files/{fileId}/download"));
        guestResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guestResp.Content.ReadAsByteArrayAsync()).ShouldBe(SamplePdf);

        // download_count tăng (owner 1 + guest 1)
        await using (var db = _app.CreateDb())
        {
            (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == docId)).DownloadCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task TeacherFileStatus_OtherTeacherGets404()
    {
        var owner = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Owner");
        var stranger = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Lạ");
        var result = await UploadAsync(Client, owner.Cookie, "rieng-tui.pdf", SamplePdf);
        result.Status.ShouldBe(HttpStatusCode.OK);
        var fileId = result.Json.GetProperty("id").GetInt64();

        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/files/{fileId}", stranger.Cookie))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/files/{fileId}", owner.Cookie))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }
}
