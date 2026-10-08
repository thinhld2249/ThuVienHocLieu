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
/// Nghiệm thu M4 (spec §6.6–6.7, §12): vòng đời lượt làm của khách — tạo, lưu tự động,
/// nộp, hết giờ, giới hạn lượt, chấm ở server, DTO không lộ đáp án.
/// Docker không khả dụng → skip (spec §16).
/// </summary>
public class AttemptTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public AttemptTests(TestApp app) => _app = app;

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

    private sealed record Actor(string Cookie);

    private static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Actor> CreateTeacherAsync(string email, string name)
    {
        var tag = NewTag();
        await using (var db = _app.CreateDb())
        {
            db.Users.Add(new User
            {
                Email = email,
                GoogleSub = $"sub-{tag}",
                FullName = name,
                SystemRole = SystemRole.Teacher,
                Status = UserStatus.Active,
            });
            await db.SaveChangesAsync();
        }
        _app.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        EnsureFactory();
        _factory!.FakeGoogle.Identities["tok-" + tag] = new GoogleIdentity("sub-" + tag, email, true, name, null, null);
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-" + tag));
        cookie.ShouldNotBeNullOrEmpty();
        return new Actor(cookie!);
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;

    private async Task<long> SectionIdAsync(string slug)
    {
        await using var db = _app.CreateDb();
        return await db.Sections.AsNoTracking()
            .Where(s => s.Slug == slug).Select(s => s.Id).FirstAsync();
    }

    /// <summary>Tạo quiz (API) + 1..n câu + publish Visible. Trả về quizId.</summary>
    private async Task<long> CreateVisibleQuizAsync(string cookie, string title,
        object[] questions, Dictionary<string, object?>? settings = null)
    {
        var sectionId = await SectionIdAsync("de-khao-sat");
        var create = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/quizzes",
            new { title, sectionId, gradeId = 5 }, cookie));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var quizId = (await JsonAsync(create)).GetProperty("id").GetInt64();

        var detail = await JsonAsync(await Client.SendAsync(
            Req(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}", cookie)));
        var put = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["sectionId"] = sectionId,
            ["gradeId"] = 5,
            ["updatedAt"] = detail.GetProperty("updatedAt").GetString(),
            ["questions"] = questions,
        };
        if (settings is not null)
            put["settings"] = settings;
        var putResp = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/quizzes/{quizId}", put, cookie));
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK, await putResp.Content.ReadAsStringAsync());

        var show = await Client.SendAsync(WriteReq(HttpMethod.Patch, $"/api/teacher/quizzes/{quizId}/publish",
            new { mode = "Visible" }, cookie));
        show.StatusCode.ShouldBe(HttpStatusCode.OK, await show.Content.ReadAsStringAsync());
        return quizId;
    }

    private static Dictionary<string, object?> SingleQ(int sort, string text,
        (string Text, bool Correct)[] options) => new()
        {
            ["sort"] = sort,
            ["type"] = "Single",
            ["contentHtml"] = $"<p>Câu {sort}: {text}</p>",
            ["options"] = options.Select((o, i) => new { sort = i + 1, contentHtml = o.Text, isCorrect = o.Correct }).ToArray(),
        };

    private static Dictionary<string, object?> MultiQ(int sort, string text,
        (string Text, bool Correct)[] options) => new()
        {
            ["sort"] = sort,
            ["type"] = "Multi",
            ["contentHtml"] = $"<p>Câu {sort}: {text}</p>",
            ["options"] = options.Select((o, i) => new { sort = i + 1, contentHtml = o.Text, isCorrect = o.Correct }).ToArray(),
        };

    /// <summary>Id các phương án (theo Sort) của từng câu quiz, từ DB — test cần biết đáp án để chọn.</summary>
    private async Task<List<List<long>>> OptionIdsByQuestionAsync(long quizId)
    {
        await using var db = _app.CreateDb();
        return await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Sort)
            .Select(q => q.Options.OrderBy(o => o.Sort).Select(o => o.Id).ToList())
            .ToListAsync();
    }

    private async Task<List<List<bool>>> CorrectFlagsByQuestionAsync(long quizId)
    {
        await using var db = _app.CreateDb();
        return await db.Questions.AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Sort)
            .Select(q => q.Options.OrderBy(o => o.Sort).Select(o => o.IsCorrect).ToList())
            .ToListAsync();
    }

    /// <summary>Tạo attempt cho khách (client cookie-jar), trả về dto + attemptId.</summary>
    private static async Task<JsonElement> CreateAttemptAsync(
        System.Net.Http.HttpClient guest, long quizId, string? guestName = "HS Khách")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/quizzes/{quizId}/attempts")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { guestName = guestName }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var resp = await guest.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return await JsonAsync(resp);
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

    private static async Task<JsonElement> SubmitAsync(System.Net.Http.HttpClient guest, string attemptId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/attempts/{attemptId}/submit")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var resp = await guest.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return await JsonAsync(resp);
    }

    // ===== Vòng đời đầy đủ + không lộ đáp án (spec §6.6–6.7, §12) =====

    [Fact]
    public async Task FullFlow_AllCorrect_Score10_ReviewVisible_NoLeakPreSubmit()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV VòngĐời");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề vòng đời", new object[]
        {
            SingleQ(1, "1 + 1 = ?", [("A. 1", false), ("B. 2", true), ("C. 3", false)]),
            SingleQ(2, "2 × 3 = ?", [("A. 5", false), ("B. 6", true)]),
        });

        var attempt = await CreateAttemptAsync(guest, quizId);
        var raw = JsonSerializer.Serialize(attempt);
        // §12: trước khi nộp, JSON không được chứa khóa đáp án/giải thích
        raw.ShouldNotContain("isCorrect");
        raw.ShouldNotContain("explanation");

        var attemptId = attempt.GetProperty("id").GetString()!;
        var qDtos = attempt.GetProperty("questions").EnumerateArray().ToArray();
        qDtos.Length.ShouldBe(2);

        // Chọn đúng: lấy id phương án đúng từ DB (khớp vị trí Sort trong DTO)
        var optionIds = await OptionIdsByQuestionAsync(quizId);
        var correct = await CorrectFlagsByQuestionAsync(quizId);
        var picks = qDtos.Select((q, i) =>
        {
            var idx = correct[i].IndexOf(true);
            return (qId: q.GetProperty("id").GetInt64(), optionIds: new[] { optionIds[i][idx] });
        }).ToArray();
        await SaveAnswersAsync(guest, attemptId, picks);

        var result = (await SubmitAsync(guest, attemptId)).GetProperty("result");
        result.GetProperty("score10").GetDecimal().ShouldBe(10m);
        result.GetProperty("correctCount").GetInt32().ShouldBe(2);
        result.GetProperty("questionCount").GetInt32().ShouldBe(2);
        result.GetProperty("canReview").GetBoolean().ShouldBeTrue(); // show_answers mặc định AfterSubmit
        var review = result.GetProperty("review").EnumerateArray().ToArray();
        review.Length.ShouldBe(2);
        foreach (var rq in review)
            foreach (var ro in rq.GetProperty("options").EnumerateArray())
                if (ro.GetProperty("selected").GetBoolean())
                    ro.GetProperty("isCorrect").GetBoolean().ShouldBeTrue();

        // Làm tiếp sau khi nộp → 409
        var again = new HttpRequestMessage(HttpMethod.Put, $"/api/public/attempts/{attemptId}/answers")
        {
            Content = new StringContent(JsonSerializer.Serialize(new[]
                { new { questionId = picks[0].qId, optionIds = picks[0].optionIds } }),
                Encoding.UTF8, "application/json"),
        };
        again.Headers.Add("X-Requested-With", "hoclieu");
        (await guest.SendAsync(again)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ===== Nhận diện (identity_mode) =====

    [Fact]
    public async Task MissingGuestName_DefaultIdentity_422()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV NhậnDiện");
        EnsureFactory();
        var guest = _factory!.CreateClient();
        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề nhận diện", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        });

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/quizzes/{quizId}/attempts")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var resp = await guest.SendAsync(req);
        resp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(resp)).GetProperty("errors").GetProperty("guestName").GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    // ===== Giới hạn lượt theo thiết bị (spec §6.6) =====

    [Fact]
    public async Task MaxAttempts_SameDeviceSecondAttempt_422()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV GiớiHạn");
        EnsureFactory();
        var guest = _factory!.CreateClient();
        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề giới hạn", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        }, new() { ["maxAttempts"] = 1 });

        // usedAttempts đếm cả lượt đang làm (giới hạn mềm theo thiết bị)
        (await CreateAttemptAsync(guest, quizId)).GetProperty("usedAttempts").GetInt32().ShouldBe(1);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/quizzes/{quizId}/attempts")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { guestName = "HS Tán" }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        var resp = await guest.SendAsync(req); // cùng cookie hl_dev → cùng thiết bị
        resp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await resp.Content.ReadAsStringAsync());
        (await JsonAsync(resp)).GetProperty("code").GetString().ShouldBe("attempt.limit");
    }

    // ===== Chấm câu chọn nhiều — Partial (spec §6.7) =====

    [Fact]
    public async Task MultiPartial_1RightOf2Correct_ScoreHalf()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Partial");
        EnsureFactory();
        var guest = _factory!.CreateClient();
        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề chọn nhiều", new object[]
        {
            MultiQ(1, "Chọn các số chẵn", [("A. 2", true), ("B. 4", true), ("C. 5", false)]),
        }, new() { ["multiScoring"] = "Partial" });

        var attempt = await CreateAttemptAsync(guest, quizId);
        var attemptId = attempt.GetProperty("id").GetString()!;
        var optionIds = (await OptionIdsByQuestionAsync(quizId)).First();
        var correct = (await CorrectFlagsByQuestionAsync(quizId)).First();

        // Chọn đúng 1/2 đáp án, không chọn sai → (1−0)/2 = 0,5 điểm
        var oneCorrect = optionIds[correct.IndexOf(true)];
        await SaveAnswersAsync(guest, attemptId, [(attempt.GetProperty("questions").EnumerateArray().Single()
            .GetProperty("id").GetInt64(), [oneCorrect])]);

        var result = (await SubmitAsync(guest, attemptId)).GetProperty("result");
        result.GetProperty("score").GetDecimal().ShouldBe(0.5m);
        result.GetProperty("score10").GetDecimal().ShouldBe(5m);
        result.GetProperty("correctCount").GetInt32().ShouldBe(0); // câu chưa đúng hoàn toàn
    }

    // ===== Hết giờ (spec §6.6): 410 + finalization =====

    [Fact]
    public async Task ExpiredAttempt_Save410_GetFinalizesExpired()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV HếtGiờ");
        EnsureFactory();
        var guest = _factory!.CreateClient();
        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề hết giờ", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        }, new() { ["timeLimitMinutes"] = 30 });

        var attempt = await CreateAttemptAsync(guest, quizId);
        var attemptId = attempt.GetProperty("id").GetString()!;

        // Ép hết hạn ở DB (mô phỏng đồng hồ chạy hết)
        await using (var db = _app.CreateDb())
        {
            var row = await db.Attempts.FirstAsync(a => a.Id == Guid.Parse(attemptId));
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var optionIds = (await OptionIdsByQuestionAsync(quizId)).First();
        var qId = attempt.GetProperty("questions").EnumerateArray().Single().GetProperty("id").GetInt64();
        var saveReq = new HttpRequestMessage(HttpMethod.Put, $"/api/public/attempts/{attemptId}/answers")
        {
            Content = new StringContent(JsonSerializer.Serialize(new[]
                { new { questionId = qId, optionIds = new[] { optionIds[0] } } }),
                Encoding.UTF8, "application/json"),
        };
        saveReq.Headers.Add("X-Requested-With", "hoclieu");
        (await guest.SendAsync(saveReq)).StatusCode.ShouldBe(HttpStatusCode.Gone);

        // GET finalization: chấm + chuyển Expired
        var got = await JsonAsync(await guest.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/public/attempts/{attemptId}")));
        got.GetProperty("status").GetString().ShouldBe("Expired");
        got.GetProperty("result").GetProperty("score10").GetDecimal().ShouldBe(0m);
    }

    // ===== show_answers = Never (spec §6.5) =====

    [Fact]
    public async Task ShowAnswersNever_ReviewNull()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Never");
        EnsureFactory();
        var guest = _factory!.CreateClient();
        var quizId = await CreateVisibleQuizAsync(teacher.Cookie, "Đề không xem đáp án", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        }, new() { ["showAnswers"] = "Never" });

        var attempt = await CreateAttemptAsync(guest, quizId);
        var attemptId = attempt.GetProperty("id").GetString()!;
        var optionIds = (await OptionIdsByQuestionAsync(quizId)).First();
        var correct = (await CorrectFlagsByQuestionAsync(quizId)).First();
        var pick = optionIds[correct.IndexOf(true)];
        await SaveAnswersAsync(guest, attemptId,
            [(attempt.GetProperty("questions").EnumerateArray().Single().GetProperty("id").GetInt64(), [pick])]);

        var result = (await SubmitAsync(guest, attemptId)).GetProperty("result");
        result.GetProperty("score10").GetDecimal().ShouldBe(10m);
        result.GetProperty("canReview").GetBoolean().ShouldBeFalse();
        result.GetProperty("review").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ===== AttemptSweeper (spec §6.6) =====

    [Fact]
    public async Task Sweeper_FinalizesExpired_AndClosedQuizAttempts()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Dọn");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        // Quiz mở: attempt quá hạn
        var openQuiz = await CreateVisibleQuizAsync(teacher.Cookie, "Đề dọn 1", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        }, new() { ["timeLimitMinutes"] = 30 });
        var openAttempt = await CreateAttemptAsync(guest, openQuiz);
        var openId = Guid.Parse(openAttempt.GetProperty("id").GetString()!);

        // Quiz ẩn (NotLive): attempt đang dở — tạo thẳng ở DB
        var hiddenQuiz = await CreateVisibleQuizAsync(teacher.Cookie, "Đề dọn 2", new object[]
        {
            SingleQ(1, "câu duy nhất", [("A. x", true), ("B. y", false)]),
        });
        var hide = await Client.SendAsync(WriteReq(HttpMethod.Patch,
            $"/api/teacher/quizzes/{hiddenQuiz}/publish", new { mode = "Hidden" }, teacher.Cookie));
        hide.StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid hiddenAttemptId;
        await using (var db = _app.CreateDb())
        {
            var now = DateTimeOffset.UtcNow;
            var a = new Attempt
            {
                QuizId = hiddenQuiz,
                DeviceId = "sweeper-device",
                GuestName = "HS Dọn",
                Layout = "null",
                Status = AttemptStatus.InProgress,
                StartedAt = now,
            };
            db.Attempts.Add(a);
            await db.SaveChangesAsync();
            hiddenAttemptId = a.Id;
            // Ép attempt quiz mở về quá hạn
            var row = await db.Attempts.FirstAsync(x => x.Id == openId);
            row.ExpiresAt = now.AddMinutes(-2);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AttemptsService>();
            var swept = await svc.SweepOnceAsync(CancellationToken.None);
            swept.ShouldBeGreaterThanOrEqualTo(2);
        }

        await using (var db = _app.CreateDb())
        {
            var attempts = await db.Attempts.AsNoTracking()
                .Where(a => a.Id == openId || a.Id == hiddenAttemptId)
                .Select(a => new { a.Status, a.Score })
                .ToListAsync();
            attempts.Count.ShouldBe(2);
            foreach (var a in attempts)
            {
                a.Status.ShouldBe(AttemptStatus.Expired);
                a.Score.ShouldNotBe(null);
            }
        }
    }
}
