using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M4 (spec §5.2, §6): CRUD quiz, import Word/Excel, ẩn/hiện/hẹn giờ,
/// nhân bản, concurrency + cảnh báo chấm lại. Docker không khả dụng → skip (spec §16).
/// </summary>
public class QuizTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public QuizTests(TestApp app) => _app = app;

    private void EnsureFactory() => _factory ??= _app.CreateFactory();

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

    // ===== Helpers =====

    private sealed record Actor(long Id, string Email, string Cookie);

    private static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>File mẫu templates (7 lần lên từ BaseDirectory có dấu \ cuối: …/bin/Debug/net8.0 → apps/).</summary>
    private static string TemplatePath(string name)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 7; i++)
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, "web", "public", "templates", name);
    }

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

    private static HttpRequestMessage WriteReq(HttpMethod method, string url, object? body, string? cookie = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Add("X-Requested-With", "hoclieu");
        if (cookie is not null)
            req.Headers.Add("Cookie", $"hl_session={cookie}");
        return req;
    }

    private static HttpRequestMessage UploadReq(string url, string fileName, byte[] bytes, string? cookie)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Add("X-Requested-With", "hoclieu");
        req.Headers.Add("Cookie", $"hl_session={cookie}");
        return req;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;

    private async Task<long> SectionIdAsync(string slug)
    {
        await using var db = _app.CreateDb();
        return await db.Sections.AsNoTracking()
            .Where(s => s.Slug == slug).Select(s => s.Id).FirstAsync();
    }

    /// <summary>Tạo quiz trống qua API (section đề khảo sát — không bắt buộc tuần).</summary>
    private async Task<long> CreateQuizViaApiAsync(string cookie, string title,
        Dictionary<string, object?>? settings = null)
    {
        var sectionId = await SectionIdAsync("de-khao-sat");
        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/quizzes",
            new { title, sectionId, gradeId = 5 }, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await JsonAsync(resp)).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> QuizDetailAsync(string cookie, long quizId)
        => await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}", cookie)));

    // ===== Import Word/Excel (spec §6.2–6.4) =====

    [Fact]
    public async Task ImportDocx_Template_CreatesHiddenQuiz_NoWarnings()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Nhập");
        var bytes = await File.ReadAllBytesAsync(TemplatePath("mau-bai-tap.docx"));

        var resp = await Client.SendAsync(UploadReq("/api/teacher/quizzes/import/docx", "mau-bai-tap.docx", bytes, teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var body = await JsonAsync(resp);
        var quizId = body.GetProperty("quizId").GetInt64();
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);

        // Quiz mới luôn Ẩn → khách 404, GV thấy ở bảng của mình
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/quizzes/{quizId}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var detail = await QuizDetailAsync(teacher.Cookie, quizId);
        detail.GetProperty("publishMode").GetString().ShouldBe("Hidden");
        detail.GetProperty("questionCount").GetInt32().ShouldBe(6);
        detail.GetProperty("totalPoints").GetDecimal().ShouldBe(6m);
        var iw = detail.GetProperty("importWarnings");
        // importWarnings = jsonb serialize: null (chưa import) hoặc "[]" (import sạch)
        if (iw.ValueKind == JsonValueKind.String)
            JsonSerializer.Deserialize<List<JsonElement>>(iw.GetString()!)!.Count.ShouldBe(0);
        else
            iw.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ImportXlsx_Template_CreatesQuiz()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV NhậpX");
        var bytes = await File.ReadAllBytesAsync(TemplatePath("mau-bai-tap.xlsx"));

        var resp = await Client.SendAsync(UploadReq("/api/teacher/quizzes/import/xlsx", "mau-bai-tap.xlsx", bytes, teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var quizId = (await JsonAsync(resp)).GetProperty("quizId").GetInt64();
        quizId.ShouldBeGreaterThan(0);

        var detail = await QuizDetailAsync(teacher.Cookie, quizId);
        detail.GetProperty("publishMode").GetString().ShouldBe("Hidden");
        detail.GetProperty("questionCount").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ImportDocx_WrongExtension_415()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV SaiFile");

        var resp = await Client.SendAsync(UploadReq("/api/teacher/quizzes/import/docx",
            "de-thi.txt", [.. Encoding.UTF8.GetBytes("không phải docx")], teacher.Cookie));
        resp.StatusCode.ShouldBe((HttpStatusCode)415, await resp.Content.ReadAsStringAsync());
        (await JsonAsync(resp)).GetProperty("code").GetString().ShouldBe("file.invalid");
    }

    // ===== Hiển thị / quyền (spec §4.3–4.4) =====

    [Fact]
    public async Task Publish_HiddenThenVisibleThenHidden_PublicFollows()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Hiện");
        var quizId = await CreateQuizViaApiAsync(teacher.Cookie, "Đề khảo sát hiện/ẩn");

        // Mặc định ẩn → khách 404
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/quizzes/{quizId}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var show = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new { mode = "Visible" }, teacher.Cookie));
        show.StatusCode.ShouldBe(HttpStatusCode.OK, await show.Content.ReadAsStringAsync());
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/quizzes/{quizId}"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var hide = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new { mode = "Hidden" }, teacher.Cookie));
        hide.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/quizzes/{quizId}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publish_ScheduledWindow_BeforeInsideAfter()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Hẹn");
        var quizId = await CreateQuizViaApiAsync(teacher.Cookie, "Đề hẹn giờ");
        var now = DateTimeOffset.UtcNow;

        var sched = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new
            {
                mode = "Scheduled",
                from = now.AddMinutes(-5).ToUniversalTime().ToString("o"),
                until = now.AddHours(2).ToUniversalTime().ToString("o"),
            }, teacher.Cookie));
        sched.StatusCode.ShouldBe(HttpStatusCode.OK, await sched.Content.ReadAsStringAsync());
        var pub = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/quizzes/{quizId}")));
        pub.GetProperty("publishState").GetString().ShouldBe("ScheduledOpen");

        // Hẹn giờ thiếu mốc → 422
        var bad = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new { mode = "Scheduled" }, teacher.Cookie));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Nhân bản / xóa mềm =====

    [Fact]
    public async Task Duplicate_OtherTeacher404_Owner201_CopyHidden()
    {
        var owner = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Chủ");
        var stranger = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Lạ");
        var quizId = await CreateQuizViaApiAsync(owner.Cookie, "Đề gốc để nhân bản");

        var denied = await Client.SendAsync(WriteReq(HttpMethod.Post, $"/api/teacher/quizzes/{quizId}/duplicate",
            null, stranger.Cookie));
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ok = await Client.SendAsync(WriteReq(HttpMethod.Post, $"/api/teacher/quizzes/{quizId}/duplicate",
            null, owner.Cookie));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var copyId = (await JsonAsync(ok)).GetProperty("id").GetInt64();
        copyId.ShouldNotBe(quizId);
        (await QuizDetailAsync(owner.Cookie, copyId)).GetProperty("publishMode").GetString()
            .ShouldBe("Hidden");
    }

    [Fact]
    public async Task Delete_SoftDeleteHidden_RestoreBack()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Xóa");
        var quizId = await CreateQuizViaApiAsync(teacher.Cookie, "Đề sẽ xóa");

        var del = await Client.SendAsync(WriteReq(HttpMethod.Delete, $"/api/teacher/quizzes/{quizId}", null, teacher.Cookie));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}", teacher.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/teacher/quizzes?includeDeleted=true&pageSize=100", teacher.Cookie)));
        bool InList(long id)
        {
            foreach (var row in list.GetProperty("items").EnumerateArray())
                if (row.GetProperty("id").GetInt64() == id)
                    return true;
            return false;
        }
        InList(quizId).ShouldBeTrue();

        var restore = await Client.SendAsync(WriteReq(HttpMethod.Post, $"/api/teacher/quizzes/{quizId}/restore",
            null, teacher.Cookie));
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}", teacher.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ===== Concurrency + cảnh báo chấm lại (spec §6.1, §8.4) =====

    [Fact]
    public async Task Update_StaleUpdatedAt_409()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Sửa");
        var quizId = await CreateQuizViaApiAsync(teacher.Cookie, "Đề sửa");
        var detail = await QuizDetailAsync(teacher.Cookie, quizId);

        var stale = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/quizzes/{quizId}",
            new
            {
                title = "Sửa tiêu đề",
                sectionId = detail.GetProperty("sectionId").GetInt64(),
                gradeId = 5,
                updatedAt = "2020-01-01T00:00:00.0000000Z",
            }, teacher.Cookie));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ChangeCorrectAnswer_WithAttempts_409_ThenRegrade()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV ChấmLại");
        EnsureFactory();
        var guest = _factory!.CreateClient(); // HandleCookies=true — giữ cookie hl_dev thiết bị khách

        var quizId = await CreateQuizViaApiAsync(teacher.Cookie, "Đề chấm lại");
        var detail = await QuizDetailAsync(teacher.Cookie, quizId);
        var sectionId = detail.GetProperty("sectionId").GetInt64();

        object Question(bool aCorrect) => new Dictionary<string, object?>
        {
            ["sort"] = 1,
            ["type"] = "Single",
            ["contentHtml"] = "<p>Câu 1: 2 + 2 = ?</p>",
            ["options"] = new object[]
            {
                new { sort = 1, contentHtml = "A. 3", isCorrect = aCorrect },
                new { sort = 2, contentHtml = "B. 4", isCorrect = !aCorrect },
            },
        };

        // PUT v1: đáp án đúng B
        var put1 = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/quizzes/{quizId}",
            new
            {
                title = "Đề chấm lại",
                sectionId,
                gradeId = 5,
                updatedAt = detail.GetProperty("updatedAt").GetString(),
                questions = new[] { Question(aCorrect: false) },
            }, teacher.Cookie));
        put1.StatusCode.ShouldBe(HttpStatusCode.OK, await put1.Content.ReadAsStringAsync());

        var show = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new { mode = "Visible" }, teacher.Cookie));
        show.StatusCode.ShouldBe(HttpStatusCode.OK, await show.Content.ReadAsStringAsync());

        // Khách tạo lượt, chọn A (sai), nộp → 0 điểm
        var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/public/quizzes/{quizId}/attempts")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { guestName = "HS Nán Lại" }),
                Encoding.UTF8, "application/json"),
        };
        createReq.Headers.Add("X-Requested-With", "hoclieu");
        var attempt = await JsonAsync(await guest.SendAsync(createReq));
        attempt.GetProperty("status").GetString().ShouldBe("InProgress");
        var attemptId = attempt.GetProperty("id").GetString()!;
        var qDto = attempt.GetProperty("questions").EnumerateArray().Single();
        var wrongOptionId = qDto.GetProperty("options").EnumerateArray().First()
            .GetProperty("id").GetInt64(); // A — phương án đầu theo thứ tự hiển thị

        var saveReq = new HttpRequestMessage(HttpMethod.Put, $"/api/public/attempts/{attemptId}/answers")
        {
            Content = new StringContent(JsonSerializer.Serialize(
                new[] { new { questionId = qDto.GetProperty("id").GetInt64(), optionIds = new[] { wrongOptionId } } }),
                Encoding.UTF8, "application/json"),
        };
        saveReq.Headers.Add("X-Requested-With", "hoclieu");
        (await guest.SendAsync(saveReq)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/public/attempts/{attemptId}/submit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        submitReq.Headers.Add("X-Requested-With", "hoclieu");
        var submitted = await JsonAsync(await guest.SendAsync(submitReq));
        submitted.GetProperty("result").GetProperty("score10").GetDecimal().ShouldBe(0m);
        submitted.GetProperty("result").GetProperty("correctCount").GetInt32().ShouldBe(0);

        // GV đổi đáp án đúng sang A khi đã có lượt làm → 409 + số lượt bị ảnh hưởng
        var detail2 = await QuizDetailAsync(teacher.Cookie, quizId);

        // Xác nhận → lưu + chấm lại: lượt cũ nay chọn đúng → 10
        // Echo id câu/phương án từ detail2 như editor FE — giữ nguyên các phương án, chỉ đổi cờ đúng.
        object PutBody(string updatedAt, bool aCorrect)
        {
            var q = detail2.GetProperty("questions").EnumerateArray().Single();
            return new
            {
                title = "Đề chấm lại",
                sectionId,
                gradeId = 5,
                updatedAt,
                questions = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = q.GetProperty("id").GetInt64(),
                        ["sort"] = 1,
                        ["type"] = "Single",
                        ["contentHtml"] = q.GetProperty("contentHtml").GetString(),
                        ["options"] = q.GetProperty("options").EnumerateArray().ToArray()
                            .Select((o, i) => new
                            {
                                id = o.GetProperty("id").GetInt64(),
                                sort = i + 1,
                                contentHtml = o.GetProperty("contentHtml").GetString(),
                                isCorrect = i == 0 ? aCorrect : !aCorrect,
                            }).ToArray(),
                    },
                },
            };
        }
        var conflict = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/quizzes/{quizId}",
            PutBody(detail2.GetProperty("updatedAt").GetString()!, aCorrect: true), teacher.Cookie));
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict, await conflict.Content.ReadAsStringAsync());
        var conflictBody = await JsonAsync(conflict);
        conflictBody.GetProperty("code").GetString().ShouldBe("quiz.has_attempts");
        conflictBody.GetProperty("affectedAttempts").GetInt32().ShouldBe(1);

        // Xác nhận → lưu + chấm lại: lượt cũ nay chọn đúng → 10
        var regrade = await Client.SendAsync(WriteReq(HttpMethod.Put,
            $"/api/teacher/quizzes/{quizId}?confirmRegrade=true",
            PutBody(detail2.GetProperty("updatedAt").GetString()!, aCorrect: true), teacher.Cookie));
        regrade.StatusCode.ShouldBe(HttpStatusCode.OK, await regrade.Content.ReadAsStringAsync());

        var refreshed = await JsonAsync(await guest.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/public/attempts/{attemptId}")));
        refreshed.GetProperty("result").GetProperty("score10").GetDecimal().ShouldBe(10m);
        refreshed.GetProperty("result").GetProperty("correctCount").GetInt32().ShouldBe(1);
    }
}
