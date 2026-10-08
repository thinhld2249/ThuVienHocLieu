using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Attempts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M5 (spec §7): giao bài bằng mã 6 ký tự — tạo/sửa/xóa assignment,
/// quiz đang Ẩn vẫn làm được qua mã (điểm M5), roster chỉ lộ khi đang mở và chỉ
/// id + fullName, nhận diện theo học sinh trong lớp, giới hạn lượt theo học sinh,
/// đóng bài → 409 + sweeper hết hạn lượt dở.
/// Docker không khả dụng → skip (spec §16).
/// </summary>
public class AssignmentTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _client;

    public AssignmentTests(TestApp app) => _app = app;

    private void EnsureFactory() => _factory ??= _app.CreateFactory();

    private System.Net.Http.HttpClient Client
    {
        get
        {
            if (!_app.DockerAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");
            EnsureFactory();
            _client ??= new Lazy<System.Net.Http.HttpClient>(() =>
                _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
            return _client.Value;
        }
    }

    // ===== Helpers =====

    private sealed record Actor(string Cookie, long Id);

    private static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Actor> CreateUserAsync(string email, string name)
    {
        var tag = NewTag();
        long userId;
        await using (var db = _app.CreateDb())
        {
            var u = new User
            {
                Email = email,
                GoogleSub = $"sub-{tag}",
                FullName = name,
                SystemRole = SystemRole.Teacher,
                Status = UserStatus.Active,
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }
        _app.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        EnsureFactory();
        _factory!.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-" + tag));
        cookie.ShouldNotBeNullOrEmpty();
        return new Actor(cookie!, userId);
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

    private static HttpRequestMessage WriteReq(HttpMethod method, string url, object? body, string? cookie = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Add("X-Requested-With", "hoclieu");
        if (cookie is not null)
            req.Headers.Add("Cookie", $"hl_session={cookie}");
        return req;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;

    private static string? ErrorCodeAsync(JsonElement problem)
        => problem.TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<long> CreateClassAsync(string cookie, string name, short gradeId = 5)
    {
        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/classes",
            new { name, gradeId }, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await JsonAsync(resp)).GetProperty("id").GetInt64();
    }

    private async Task<long> CreateStudentAsync(string cookie, long classId, string name)
    {
        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, $"/api/teacher/classes/{classId}/students",
            new { fullName = name }, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await JsonAsync(resp)).GetProperty("id").GetInt64();
    }

    private async Task<long> CreateQuizAsync(string cookie, string title, bool visible,
        Dictionary<string, object?>? settings = null, string? scope = null)
    {
        await using var db = _app.CreateDb();
        var sectionId = await db.Sections.AsNoTracking()
            .Where(s => s.Slug == "de-khao-sat").Select(s => s.Id).FirstAsync();

        var create = new Dictionary<string, object?> { ["title"] = title, ["sectionId"] = sectionId, ["gradeId"] = 5 };
        if (scope is not null)
            create["scope"] = scope;
        var createResp = await Client.SendAsync(
            WriteReq(HttpMethod.Post, "/api/teacher/quizzes", create, cookie));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created, await createResp.Content.ReadAsStringAsync());
        var quizId = (await JsonAsync(createResp)).GetProperty("id").GetInt64();

        var detail = await JsonAsync(await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}", null, cookie)));
        var put = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["sectionId"] = sectionId,
            ["gradeId"] = 5,
            ["updatedAt"] = detail.GetProperty("updatedAt").GetString(),
            ["questions"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["sort"] = 1,
                    ["type"] = "Single",
                    ["contentHtml"] = "<p>Câu 1: 2 + 2 = ?</p>",
                    ["options"] = new object[]
                    {
                        new { sort = 1, contentHtml = "A. 3", isCorrect = false },
                        new { sort = 2, contentHtml = "B. 4", isCorrect = true },
                    },
                },
            },
        };
        if (settings is not null)
            put["settings"] = settings;
        if (scope is not null)
            put["scope"] = scope;
        var putResp = await Client.SendAsync(
            WriteReq(HttpMethod.Put, $"/api/teacher/quizzes/{quizId}", put, cookie));
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK, await putResp.Content.ReadAsStringAsync());

        if (visible)
        {
            var show = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
                new { mode = "Visible" }, cookie));
            show.StatusCode.ShouldBe(HttpStatusCode.OK, await show.Content.ReadAsStringAsync());
        }
        return quizId;
    }

    private async Task<JsonElement> CreateAssignmentAsync(string cookie, long classId, long quizId,
        bool useRoster = true, DateTimeOffset? openAt = null, DateTimeOffset? closeAt = null)
    {
        var body = new Dictionary<string, object?> { ["quizId"] = quizId, ["useRoster"] = useRoster };
        if (openAt is { } o) body["openAt"] = o;
        if (closeAt is { } c) body["closeAt"] = c;
        var resp = await Client.SendAsync(
            WriteReq(HttpMethod.Post, $"/api/teacher/classes/{classId}/assignments", body, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return await JsonAsync(resp);
    }

    private static Task<HttpResponseMessage> AttemptByCodeAsync(
        System.Net.Http.HttpClient guest, string code, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/assignments/{code}/attempts")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        return guest.SendAsync(req);
    }

    private static async Task SaveAnswersAsync(
        System.Net.Http.HttpClient guest, string attemptId, (long QuestionId, long[] OptionIds)[] answers)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/public/attempts/{attemptId}/answers")
        {
            Content = new StringContent(JsonSerializer.Serialize(answers
                    .Select(a => new { questionId = a.QuestionId, optionIds = a.OptionIds })),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var resp = await guest.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private async Task<(long QuestionId, long CorrectOptionId)> QuestionAndCorrectOptionAsync(long quizId)
    {
        await using var db = _app.CreateDb();
        var q = await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .Select(q => new
            {
                q.Id,
                Correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).First(),
            })
            .FirstAsync();
        return (q.Id, q.Correct);
    }

    // ===== Tạo assignment: quyền quiz + giờ mở/đóng + định dạng mã (spec §7) =====

    [Fact]
    public async Task CreateAssignment_OwnAndPublicQuizAllowed_ForeignNonPublic422_BadTimes422()
    {
        var a = await CreateUserAsync($"a-{NewTag()}@gmail.com", "GV A");
        var b = await CreateUserAsync($"b-{NewTag()}@gmail.com", "GV B");
        var c = await CreateUserAsync($"c-{NewTag()}@gmail.com", "GV C");

        var classA = await CreateClassAsync(a.Cookie, "5A");
        var classB = await CreateClassAsync(b.Cookie, "5B");
        var classC = await CreateClassAsync(c.Cookie, "5C");

        // Quiz Public của A → B (GV khác) được giao
        var publicQuiz = await CreateQuizAsync(a.Cookie, "Quiz công khai A", visible: true);
        var ok1 = await CreateAssignmentAsync(b.Cookie, classB, publicQuiz);
        ok1.GetProperty("quizId").GetInt64().ShouldBe(publicQuiz);

        // Quiz phạm vi Teachers của B → chính B (chủ sở hữu) được giao
        var privateQuiz = await CreateQuizAsync(b.Cookie, "Quiz nội bộ B", visible: true, scope: "Teachers");
        var ok2 = await CreateAssignmentAsync(b.Cookie, classB, privateQuiz);
        ok2.GetProperty("quizId").GetInt64().ShouldBe(privateQuiz);

        // Quiz Teachers của B → C (không sở hữu, không Public) → 422 quizId (lớp của chính C)
        var forbidden = await Client.SendAsync(WriteReq(HttpMethod.Post,
            $"/api/teacher/classes/{classC}/assignments", new { quizId = privateQuiz, useRoster = true }, c.Cookie));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(forbidden)).GetProperty("errors").GetProperty("quizId").GetArrayLength()
            .ShouldBeGreaterThan(0);

        // Giờ đóng không sau giờ mở → 422 closeAt
        var bad = await Client.SendAsync(WriteReq(HttpMethod.Post,
            $"/api/teacher/classes/{classA}/assignments",
            new
            {
                quizId = publicQuiz,
                useRoster = true,
                openAt = DateTimeOffset.UtcNow.AddHours(1),
                closeAt = DateTimeOffset.UtcNow,
            }, a.Cookie));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(bad)).GetProperty("errors").GetProperty("closeAt").GetArrayLength()
            .ShouldBeGreaterThan(0);

        // Mã 6 ký tự, không có 0/O/1/I/L
        var code = ok1.GetProperty("code").GetString()!;
        code.Length.ShouldBe(6);
        foreach (var ch in code)
            "01IO L".ShouldNotContain(ch);
    }

    // ===== Quiz ẨN vẫn làm được qua mã giao bài — nghiệm thu M5 (spec §7) =====

    [Fact]
    public async Task HiddenQuiz_AttemptableViaCode_PublicQuizUrl404()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV GiaoBài");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        var classId = await CreateClassAsync(t.Cookie, "1A");
        var hs = await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A");
        var quizId = await CreateQuizAsync(t.Cookie, "Quiz ẩn cho lớp", visible: false);
        var assignment = await CreateAssignmentAsync(t.Cookie, classId, quizId);
        var code = assignment.GetProperty("code").GetString()!;

        // Quiz ẩn → URL công khai 404
        var publicQuiz = await _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false })
            .GetAsync($"/api/public/quizzes/{quizId}");
        publicQuiz.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nhưng qua mã giao bài → làm bài bình thường
        var attempt = await JsonAsync(await AttemptByCodeAsync(guest, code, new { studentId = hs }));
        attempt.GetProperty("status").GetString().ShouldBe("InProgress");
        var raw = JsonSerializer.Serialize(attempt);
        raw.ShouldNotContain("isCorrect");

        var (qId, correctOption) = await QuestionAndCorrectOptionAsync(quizId);
        await SaveAnswersAsync(guest, attempt.GetProperty("id").GetString()!, [(qId, [correctOption])]);
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/public/attempts/{attempt.GetProperty("id").GetString()}/submit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var result = await JsonAsync(await guest.SendAsync(req));
        result.GetProperty("result").GetProperty("score10").GetDecimal().ShouldBe(10m);
    }

    // ===== Roster chỉ lộ khi đang mở, chỉ id + fullName (spec §7, §12) =====

    [Fact]
    public async Task PublicRoster_ExposedOnlyWhileOpen_NoPii()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV Roster");
        EnsureFactory();
        var anon = _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var classId = await CreateClassAsync(t.Cookie, "2A");
        await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A");
        await CreateStudentAsync(t.Cookie, classId, "Trần Thị B");
        var quizId = await CreateQuizAsync(t.Cookie, "Quiz roster", visible: true);

        // Đang mở
        var open = await CreateAssignmentAsync(t.Cookie, classId, quizId);
        var codeOpen = open.GetProperty("code").GetString()!;
        var gotOpen = await JsonAsync(await anon.GetAsync($"/api/public/assignments/{codeOpen}"));
        gotOpen.GetProperty("status").GetString().ShouldBe("Open");
        var roster = gotOpen.GetProperty("roster").EnumerateArray().ToArray();
        roster.Length.ShouldBe(2);
        roster.Select(r => r.GetProperty("fullName").GetString()).ShouldBe(
            ["Nguyễn Văn A", "Trần Thị B"]);
        var raw = JsonSerializer.Serialize(gotOpen);
        raw.ShouldNotContain("dateOfBirth");
        raw.ShouldNotContain("gender");

        // Chưa mở (Sắp mở) → không có roster
        var scheduled = await CreateAssignmentAsync(t.Cookie, classId, quizId,
            openAt: DateTimeOffset.UtcNow.AddHours(1), closeAt: DateTimeOffset.UtcNow.AddHours(2));
        var gotScheduled = await JsonAsync(await anon.GetAsync(
            $"/api/public/assignments/{scheduled.GetProperty("code").GetString()}"));
        gotScheduled.GetProperty("status").GetString().ShouldBe("Scheduled");
        gotScheduled.GetProperty("roster").ValueKind.ShouldBe(JsonValueKind.Null);

        // Đã đóng → không có roster
        var closed = await CreateAssignmentAsync(t.Cookie, classId, quizId,
            openAt: DateTimeOffset.UtcNow.AddHours(-2), closeAt: DateTimeOffset.UtcNow.AddHours(-1));
        var gotClosed = await JsonAsync(await anon.GetAsync(
            $"/api/public/assignments/{closed.GetProperty("code").GetString()}"));
        gotClosed.GetProperty("status").GetString().ShouldBe("Closed");
        gotClosed.GetProperty("roster").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ===== Nhận diện qua roster: bắt buộc studentId, đúng lớp (spec §7) =====

    [Fact]
    public async Task AttemptByCode_RosterValidation_WrongCode404()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV NhậnDiện");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        var classId = await CreateClassAsync(t.Cookie, "3A");
        var hs = await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A");
        var otherClass = await CreateClassAsync(t.Cookie, "3B");
        var otherHs = await CreateStudentAsync(t.Cookie, otherClass, "Lê Văn C");
        var quizId = await CreateQuizAsync(t.Cookie, "Quiz nhận diện", visible: true);
        var code = (await CreateAssignmentAsync(t.Cookie, classId, quizId)).GetProperty("code").GetString()!;

        // Thiếu studentId → 422
        var noId = await AttemptByCodeAsync(guest, code, new { guestName = "Người không tên" });
        noId.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(noId)).GetProperty("errors").GetProperty("studentId").GetArrayLength()
            .ShouldBeGreaterThan(0);

        // Học sinh của lớp khác → 422
        var wrongClass = await AttemptByCodeAsync(guest, code, new { studentId = otherHs });
        wrongClass.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(wrongClass)).GetProperty("errors").GetProperty("studentId").GetArrayLength()
            .ShouldBeGreaterThan(0);

        // Mã sai → 404
        var badCode = await AttemptByCodeAsync(guest, "ZZZZZZ", new { studentId = hs });
        badCode.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ===== Giới hạn lượt theo học sinh, không theo thiết bị (spec §6.6, §7) =====

    [Fact]
    public async Task AttemptLimit_ByStudentId_NotPerDevice()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV GiớiHạn");
        EnsureFactory();
        var guest1 = _factory!.CreateClient();
        var guest2 = _factory.CreateClient();

        var classId = await CreateClassAsync(t.Cookie, "4A");
        var s1 = await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A");
        var s2 = await CreateStudentAsync(t.Cookie, classId, "Trần Thị B");
        var quizId = await CreateQuizAsync(t.Cookie, "Quiz giới hạn", visible: false,
            settings: new() { ["maxAttempts"] = 1 });
        var code = (await CreateAssignmentAsync(t.Cookie, classId, quizId)).GetProperty("code").GetString()!;

        var first = await AttemptByCodeAsync(guest1, code, new { studentId = s1 });
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        // Cùng học sinh (thiết bị khác) → hết lượt
        var second = await AttemptByCodeAsync(guest2, code, new { studentId = s1 });
        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ErrorCodeAsync(await JsonAsync(second)).ShouldBe("attempt.limit");

        // Học sinh khác → được làm
        var other = await AttemptByCodeAsync(guest2, code, new { studentId = s2 });
        other.StatusCode.ShouldBe(HttpStatusCode.OK, await other.Content.ReadAsStringAsync());
    }

    // ===== Đóng bài: lưu/nộp → 409, sweeper chấm & hết hạn lượt dở (spec §6.6) =====

    [Fact]
    public async Task ClosedAssignment_SaveSubmit409_SweeperExpiresInProgress()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV ĐóngBài");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        var classId = await CreateClassAsync(t.Cookie, "6C");
        var hs = await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A");
        var quizId = await CreateQuizAsync(t.Cookie, "Quiz đóng", visible: true);
        var code = (await CreateAssignmentAsync(t.Cookie, classId, quizId)).GetProperty("code").GetString()!;

        var attempt = await JsonAsync(await AttemptByCodeAsync(guest, code, new { studentId = hs }));
        var attemptId = attempt.GetProperty("id").GetString()!;
        var (qId, correctOption) = await QuestionAndCorrectOptionAsync(quizId);

        // Ép assignment đã đóng (sau khi tạo attempt — mô phỏng hết giờ đóng)
        await using (var db = _app.CreateDb())
        {
            var a = await db.Assignments.FirstAsync(x => x.Code == code);
            a.CloseAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            a.OpenAt = DateTimeOffset.UtcNow.AddMinutes(-60);
            await db.SaveChangesAsync();
        }

        // Lưu câu trả lời sau giờ đóng → 409
        var save = new HttpRequestMessage(HttpMethod.Put, $"/api/public/attempts/{attemptId}/answers")
        {
            Content = new StringContent(JsonSerializer.Serialize(new[]
                { new { questionId = qId, optionIds = new[] { correctOption } } }),
                Encoding.UTF8, "application/json"),
        };
        save.Headers.Add("X-Requested-With", "hoclieu");
        (await guest.SendAsync(save)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Nộp sau giờ đóng → 409
        var submit = new HttpRequestMessage(HttpMethod.Post, $"/api/public/attempts/{attemptId}/submit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        submit.Headers.Add("X-Requested-With", "hoclieu");
        (await guest.SendAsync(submit)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Tạo thêm một lượt dở nữa, rồi chạy sweeper
        Guid secondAttemptId;
        await using (var db = _app.CreateDb())
        {
            var now = DateTimeOffset.UtcNow;
            var a = new Attempt
            {
                QuizId = quizId,
                AssignmentId = (await db.Assignments.AsNoTracking().FirstAsync(x => x.Code == code)).Id,
                StudentId = hs,
                DeviceId = "sweeper-assign-device",
                Layout = "null",
                Status = AttemptStatus.InProgress,
                StartedAt = now,
            };
            db.Attempts.Add(a);
            await db.SaveChangesAsync();
            secondAttemptId = a.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AttemptsService>();
            (await svc.SweepOnceAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        }

        await using (var db = _app.CreateDb())
        {
            var row = await db.Attempts.AsNoTracking().FirstAsync(x => x.Id == secondAttemptId);
            row.Status.ShouldBe(AttemptStatus.Expired);
            row.Score.ShouldNotBe(null);
        }
    }
}
