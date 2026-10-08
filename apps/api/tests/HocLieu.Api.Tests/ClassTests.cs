using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
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
/// Nghiệm thu M5 (spec §7): lớp học & học sinh — CRUD lớp, quyền truy cập lớp
/// (chủ nhiệm / bộ môn / 403 vs 404 / Admin), nhập danh sách HS từ Excel
/// (dryRun không ghi, báo lỗi từng dòng, trùng bỏ qua), xuất Excel,
/// bảng điểm (ô = điểm cao nhất, null = chưa làm) + xuất Excel.
/// Docker không khả dụng → skip (spec §16).
/// </summary>
public class ClassTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _client;

    public ClassTests(TestApp app) => _app = app;

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

    private async Task<Actor> CreateUserAsync(string email, string name, SystemRole role = SystemRole.Teacher)
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
                SystemRole = role,
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

    private async Task<long> CreateClassAsync(string cookie, string name, short gradeId = 5,
        (long UserId, long? SubjectId)[]? teachers = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["gradeId"] = gradeId,
        };
        if (teachers is not null)
            body["teachers"] = teachers
                .Select(t => new Dictionary<string, object?> { ["userId"] = t.UserId, ["subjectId"] = t.SubjectId })
                .ToList();
        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/classes", body, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await JsonAsync(resp)).GetProperty("id").GetInt64();
    }

    private async Task<long> CreateStudentAsync(string cookie, long classId, string name, string? dob = null)
    {
        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, $"/api/teacher/classes/{classId}/students",
            new Dictionary<string, object?> { ["fullName"] = name, ["dateOfBirth"] = dob }, cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await JsonAsync(resp)).GetProperty("id").GetInt64();
    }

    private async Task<long> CreateQuizAsync(string cookie, string title, object[] questions,
        bool visible, Dictionary<string, object?>? settings = null, string? scope = null)
    {
        var sectionId = await SectionIdAsync("de-khao-sat");
        var create = new Dictionary<string, object?> { ["title"] = title, ["sectionId"] = sectionId, ["gradeId"] = 5 };
        if (scope is not null)
            create["scope"] = scope;
        var createResp = await Client.SendAsync(
            WriteReq(HttpMethod.Post, "/api/teacher/quizzes", create, cookie));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created, await createResp.Content.ReadAsStringAsync());
        var quizId = (await JsonAsync(createResp)).GetProperty("id").GetInt64();

        var detail = await JsonAsync(await Client.SendAsync(WriteReq(HttpMethod.Get, $"/api/teacher/quizzes/{quizId}",
            null, cookie)));
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

    private async Task<long> SectionIdAsync(string slug)
    {
        await using var db = _app.CreateDb();
        return await db.Sections.AsNoTracking()
            .Where(s => s.Slug == slug).Select(s => s.Id).FirstAsync();
    }

    private static Dictionary<string, object?> SingleQ(int sort, string text,
        (string Text, bool Correct)[] options) => new()
        {
            ["sort"] = sort,
            ["type"] = "Single",
            ["contentHtml"] = $"<p>Câu {sort}: {text}</p>",
            ["options"] = options.Select((o, i) => new { sort = i + 1, contentHtml = o.Text, isCorrect = o.Correct }).ToArray(),
        };

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

    /// <summary>Sinh file danh sách HS (sheet "Danh sach HS", dòng 1 tiêu đề). Ngày sinh đặt bằng SetText để giữ dạng chuỗi.</summary>
    private static byte[] BuildRosterXlsx(params (string Name, string Dob, string Gender, string Code)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sach HS");
        string[] headers = { "STT", "Họ và tên", "Ngày sinh", "Giới tính", "Mã HS" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        var r = 2;
        foreach (var (name, dob, gender, code) in rows)
        {
            ws.Cell(r, 1).Value = r - 1;
            ws.Cell(r, 2).Value = name;
            // Chuỗi dạng ngày không bị ClosedXML tự chuyển sang kiểu date (đã xác minh)
            ws.Cell(r, 3).Value = dob;
            ws.Cell(r, 4).Value = gender;
            ws.Cell(r, 5).Value = code;
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadRosterAsync(
        System.Net.Http.HttpClient client, long classId, byte[] xlsx, bool dryRun, string cookie)
    {
        // Không dispose form trước khi request gửi xong (MemoryStream sẽ bị khóa)
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(xlsx), "file", "danh-sach-hs.xlsx");
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/teacher/classes/{classId}/students/import?dryRun={dryRun.ToString().ToLowerInvariant()}")
        {
            Content = form,
        };
        req.Headers.Add("X-Requested-With", "hoclieu");
        req.Headers.Add("Cookie", $"hl_session={cookie}");
        return await client.SendAsync(req);
    }

    /// <summary>Tạo attempt cho học sinh trong lớp qua mã giao bài (client guest có cookie-jar cho hl_dev).</summary>
    private static async Task<JsonElement> AttemptByCodeAsync(
        System.Net.Http.HttpClient guest, string code, long? studentId, string? guestName = null)
    {
        var body = new Dictionary<string, object?>();
        if (studentId is { } sid)
            body["studentId"] = sid;
        if (guestName is not null)
            body["guestName"] = guestName;
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/public/assignments/{code}/attempts")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
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

    /// <summary>Làm bài qua mã: chọn đúng/sai theo chỉ thị, trả về score10.</summary>
    private async Task<decimal> DoAttemptAsync(
        System.Net.Http.HttpClient guest, string code, long studentId, long quizId, bool allCorrect)
    {
        var attempt = await AttemptByCodeAsync(guest, code, studentId);
        var attemptId = attempt.GetProperty("id").GetString()!;
        var qDtos = attempt.GetProperty("questions").EnumerateArray().ToArray();
        var optionIds = await OptionIdsByQuestionAsync(quizId);
        var correct = await CorrectFlagsByQuestionAsync(quizId);
        var picks = qDtos.Select((q, i) =>
        {
            var idx = allCorrect ? correct[i].IndexOf(true) : correct[i].IndexOf(false);
            return (qId: q.GetProperty("id").GetInt64(), optionIds: new[] { optionIds[i][idx] });
        }).ToArray();
        await SaveAnswersAsync(guest, attemptId, picks);
        var result = (await SubmitAsync(guest, attemptId)).GetProperty("result");
        return result.GetProperty("score10").GetDecimal();
    }

    // ===== Lớp: CRUD + tên duy nhất theo năm học =====

    [Fact]
    public async Task CreateClass_OwnerIsHomeroom_NameUniquePerYear()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV TạoLớp");
        var classId = await CreateClassAsync(t.Cookie, "5A");

        var detail = await JsonAsync(await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}", null, t.Cookie)));
        detail.GetProperty("name").GetString().ShouldBe("5A");
        detail.GetProperty("homeroomTeacherId").GetInt64().ShouldBe(t.Id);
        detail.GetProperty("homeroomTeacherName").GetString().ShouldBe("GV TạoLớp");

        // Tên trùng trong cùng năm học → 422
        var dup = await Client.SendAsync(
            WriteReq(HttpMethod.Post, "/api/teacher/classes", new { name = "5A", gradeId = 5 }, t.Cookie));
        dup.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(dup)).GetProperty("errors").GetProperty("name").GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    // ===== Quyền truy cập lớp (spec §2.3 CanAccessClass) =====

    [Fact]
    public async Task ClassAccess_HomeroomAndSubjectTeacherOk_Other403_NotFound404_AdminOk()
    {
        var a = await CreateUserAsync($"a-{NewTag()}@gmail.com", "GV ChủNhiệm");
        var b = await CreateUserAsync($"b-{NewTag()}@gmail.com", "GV BộMôn");
        var c = await CreateUserAsync($"c-{NewTag()}@gmail.com", "GV VôQuan");
        var admin = await CreateUserAsync($"ad-{NewTag()}@gmail.com", "Admin Lớp", SystemRole.Admin);

        long subjectId;
        await using (var db = _app.CreateDb())
            subjectId = await db.Subjects.AsNoTracking().Select(s => s.Id).FirstAsync();

        var classId = await CreateClassAsync(a.Cookie, "4B", teachers: [(b.Id, subjectId)]);

        (await JsonAsync(await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}", null, a.Cookie))))
            .GetProperty("id").GetInt64().ShouldBe(classId); // chủ nhiệm
        (await JsonAsync(await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}", null, b.Cookie))))
            .GetProperty("id").GetInt64().ShouldBe(classId); // GV bộ môn

        // GV khác thấy lớp tồn tại nhưng không có quyền → 403 (không 404)
        var forbidden = await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}", null, c.Cookie));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonAsync(forbidden)).GetProperty("code").GetString().ShouldBe("class.no_access");

        // Lớp không tồn tại → 404
        var missing = await Client.SendAsync(
            WriteReq(HttpMethod.Get, "/api/teacher/classes/999999999", null, c.Cookie));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await JsonAsync(await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}", null, admin.Cookie))))
            .GetProperty("id").GetInt64().ShouldBe(classId); // Admin
    }

    // ===== Nhập danh sách HS — dryRun không ghi (spec §7) =====

    [Fact]
    public async Task StudentsImport_DryRun_NoWrites_RowErrors_AndDuplicates()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV NhậpHS");
        var classId = await CreateClassAsync(t.Cookie, "1C");

        var xlsx = BuildRosterXlsx(
            ("Nguyễn Văn A", "10/05/2017", "Nam", "HS001"),
            ("", "01/02/2017", "", ""),                 // dòng 3: thiếu họ tên
            ("Trần Thị B", "31/02/2017", "Nữ", "HS002"), // dòng 4: ngày sinh sai
            ("Nguyễn Văn A", "10/05/2017", "", "")       // dòng 5: trùng trong file
        );

        var resp = await UploadRosterAsync(Client, classId, xlsx, dryRun: true, t.Cookie);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var result = await JsonAsync(resp);

        result.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        result.GetProperty("importCount").GetInt32().ShouldBe(1);
        result.GetProperty("duplicateCount").GetInt32().ShouldBe(1);
        var errors = result.GetProperty("errors").EnumerateArray().ToArray();
        errors.Length.ShouldBe(3);
        errors.Select(e => e.GetProperty("row").GetInt32()).OrderBy(x => x)
            .ShouldBe([3, 4, 5]);
        errors.Single(e => e.GetProperty("row").GetInt32() == 3)
            .GetProperty("message").GetString()!.ShouldContain("Thiếu họ tên");
        errors.Single(e => e.GetProperty("row").GetInt32() == 4)
            .GetProperty("message").GetString()!.ShouldContain("Ngày sinh");

        // dryRun → không ghi gì
        await using (var db = _app.CreateDb())
            (await db.Students.CountAsync(s => s.ClassId == classId)).ShouldBe(0);
    }

    // ===== Nhập commit: ordinal gán tuần tự, import lại → trùng toàn bộ =====

    [Fact]
    public async Task StudentsImport_Commit_OrdinalsAssigned_ReimportAllDuplicates()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV NhậpHS2");
        var classId = await CreateClassAsync(t.Cookie, "2D");

        var xlsx = BuildRosterXlsx(
            ("Nguyễn Văn A", "10/05/2017", "Nam", "HS001"),
            ("Trần Thị B", "01/02/2017", "Nữ", "HS002"),
            ("Lê Văn C", "15/08/2017", "Nam", "HS003")
        );

        var commit = await UploadRosterAsync(Client, classId, xlsx, dryRun: false, t.Cookie);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());
        var first = await JsonAsync(commit);
        first.GetProperty("dryRun").GetBoolean().ShouldBeFalse();
        first.GetProperty("importCount").GetInt32().ShouldBe(3);
        first.GetProperty("duplicateCount").GetInt32().ShouldBe(0);
        first.GetProperty("errors").GetArrayLength().ShouldBe(0);

        // Import lại cùng file → mọi dòng hợp lệ đều trùng lớp; dòng trong-file trùng vẫn báo
        var again = await UploadRosterAsync(Client, classId, xlsx, dryRun: false, t.Cookie);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var second = await JsonAsync(again);
        second.GetProperty("importCount").GetInt32().ShouldBe(0);
        second.GetProperty("duplicateCount").GetInt32().ShouldBe(3);

        await using (var db = _app.CreateDb())
        {
            (await db.Students.CountAsync(s => s.ClassId == classId)).ShouldBe(3);
            var ordinals = await db.Students.AsNoTracking()
                .Where(s => s.ClassId == classId)
                .OrderBy(s => s.Ordinal)
                .Select(s => new { s.FullName, s.Ordinal })
                .ToListAsync();
            ordinals.Select(o => o.Ordinal).ShouldBe([1, 2, 3]);
            ordinals[0].FullName.ShouldBe("Nguyễn Văn A");
        }
    }

    // ===== Xuất danh sách HS (xlsx) =====

    [Fact]
    public async Task StudentsExport_XlsxRoundtrip()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV XuấtHS");
        var classId = await CreateClassAsync(t.Cookie, "3E");
        await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A", "2017-05-10");
        await CreateStudentAsync(t.Cookie, classId, "Trần Thị B", "2017-02-01");

        var resp = await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}/students/export.xlsx", null, t.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await resp.Content.ReadAsByteArrayAsync();

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        ws!.Name.ShouldBe("Danh sach HS");
        ws.Cell(1, 2).GetString().ShouldBe("Họ và tên");
        ws.Cell(2, 2).GetString().ShouldBe("Nguyễn Văn A");
        ws.Cell(2, 3).GetString().ShouldBe("10/05/2017");
        ws.Cell(3, 2).GetString().ShouldBe("Trần Thị B");
    }

    // ===== Bảng điểm: ô = điểm cao nhất, null = chưa làm (spec §7) =====

    [Fact]
    public async Task Gradebook_MaxScorePerCell_NullForNotDone_ExportXlsx()
    {
        var t = await CreateUserAsync($"t-{NewTag()}@gmail.com", "GV BảngĐiểm");
        EnsureFactory();
        var guest = _factory!.CreateClient();

        var classId = await CreateClassAsync(t.Cookie, "5B");
        var hs1 = await CreateStudentAsync(t.Cookie, classId, "Nguyễn Văn A", "2012-05-10");
        var hs2 = await CreateStudentAsync(t.Cookie, classId, "Trần Thị B", "2012-02-01");

        var quiz1 = await CreateQuizAsync(t.Cookie, "Đề tuần 1", new object[]
        {
            SingleQ(1, "1 + 1 = ?", [("A. 1", false), ("B. 2", true)]),
            SingleQ(2, "2 + 2 = ?", [("A. 5", false), ("B. 4", true)]),
        }, visible: true);
        var quiz2 = await CreateQuizAsync(t.Cookie, "Đề tuần 2", new object[]
        {
            SingleQ(1, "3 × 2 = ?", [("A. 6", true), ("B. 5", false)]),
        }, visible: true);

        var a1 = await CreateAssignmentAsync(t.Cookie, classId, quiz1);
        var a2 = await CreateAssignmentAsync(t.Cookie, classId, quiz2);
        var code1 = a1.GetProperty("code").GetString()!;
        var code2 = a2.GetProperty("code").GetString()!;

        // HS1: làm bài 1 đúng (10) rồi làm lại sai (0) → ô phải là max = 10; bài 2 đúng (10)
        (await DoAttemptAsync(guest, code1, hs1, quiz1, allCorrect: true)).ShouldBe(10m);
        (await DoAttemptAsync(guest, code1, hs1, quiz1, allCorrect: false)).ShouldBe(0m);
        (await DoAttemptAsync(guest, code2, hs1, quiz2, allCorrect: true)).ShouldBe(10m);
        // HS2: chưa làm

        var gbResp = await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}/gradebook", null, t.Cookie));
        gbResp.StatusCode.ShouldBe(HttpStatusCode.OK, await gbResp.Content.ReadAsStringAsync());
        var gb = await JsonAsync(gbResp);
        gb.GetProperty("className").GetString().ShouldBe("5B");

        var columns = gb.GetProperty("columns").EnumerateArray().ToArray();
        columns.Length.ShouldBe(2);
        var idx1 = columns.Single(c => c.GetProperty("quizTitle").GetString() == "Đề tuần 1")
            .GetProperty("code").GetString()!;
        var idx2 = columns.Single(c => c.GetProperty("quizTitle").GetString() == "Đề tuần 2")
            .GetProperty("code").GetString()!;
        idx1.ShouldBe(code1);
        idx2.ShouldBe(code2);

        var students = gb.GetProperty("students").EnumerateArray().ToArray();
        students.Length.ShouldBe(2);
        var scores = gb.GetProperty("scores").EnumerateArray().ToArray();
        scores.Length.ShouldBe(2);
        // HS1 (dòng đầu theo ordinal): bài 1 = max(10, 0) = 10, bài 2 = 10
        var row1 = scores[0].EnumerateArray().ToArray();
        row1.Length.ShouldBe(2);
        row1.All(x => x.ValueKind == JsonValueKind.Number).ShouldBeTrue();
        row1[0].GetDecimal().ShouldBe(10m);
        row1[1].GetDecimal().ShouldBe(10m);
        // HS2: cả hai ô null
        var row2 = scores[1].EnumerateArray().ToArray();
        row2.All(x => x.ValueKind == JsonValueKind.Null).ShouldBeTrue();

        // Xuất Excel bảng điểm
        var xl = await Client.SendAsync(
            WriteReq(HttpMethod.Get, $"/api/teacher/classes/{classId}/gradebook.xlsx", null, t.Cookie));
        xl.StatusCode.ShouldBe(HttpStatusCode.OK);
        xl.Content.Headers.ContentType?.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await xl.Content.ReadAsByteArrayAsync()).Length.ShouldBeGreaterThan(2000);
    }
}
