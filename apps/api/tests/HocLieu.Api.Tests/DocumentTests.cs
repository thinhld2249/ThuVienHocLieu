using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M3 (spec §4, §5.1–5.2, §10): CRUD tài liệu, phạm vi/ẩn-hiện/hẹn giờ,
/// duyệt công khai + tìm kiếm không dấu, chi tiết/liên quan/báo lỗi, yêu thích, kiểm duyệt.
/// Docker không khả dụng → skip (spec §16).
/// </summary>
public class DocumentTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public DocumentTests(TestApp app) => _app = app;

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

    /// <summary>Request ghi (POST/PUT/PATCH/DELETE) — bắt buộc header CSRF (spec §3.4).</summary>
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

    /// <summary>Tạo tài liệu thẳng ở DB (đủ nhanh cho test phân quyền; column search_text tự tính).</summary>
    private async Task<Document> CreateDocInDbAsync(
        string title, long ownerId, long sectionId, ContentScope scope, PublishMode mode,
        long? teamId = null, short? weekNo = null,
        DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        await using var db = _app.CreateDb();
        var year = await db.SchoolYears.AsNoTracking().FirstAsync(y => y.IsCurrent);
        var doc = new Document
        {
            Title = title,
            Slug = "doc-" + NewTag(),
            SectionId = sectionId,
            GradeId = 5,
            SchoolYearId = year.Id,
            WeekNo = weekNo,
            OwnerId = ownerId,
            TeamId = teamId,
            Scope = scope,
            PublishMode = mode,
            PublishFrom = from,
            PublishUntil = until,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private async Task<long> CreateFileInDbAsync(long ownerId, int i)
    {
        await using var db = _app.CreateDb();
        var f = new FileEntity
        {
            OwnerId = ownerId,
            OriginalName = $"f{i}.pdf",
            Ext = "pdf",
            Mime = "application/pdf",
            Bytes = 100,
            Sha256 = Guid.NewGuid().ToString("N"),
            StoragePublicId = $"tests/{NewTag()}/{i}",
            StorageResourceType = "image",
            ProcessingStatus = ProcessingStatus.Ready,
            PreviewPages = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private async Task<long> CreateTeamAsync(long leadId, TeamRole leadRole = TeamRole.Lead)
    {
        await using var db = _app.CreateDb();
        var team = new Team
        {
            Name = "Tổ " + NewTag(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = leadId,
            Role = leadRole,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return team.Id;
    }

    private static object DocBody(long sectionId, string title, string? weekNo = null) =>
        new Dictionary<string, object?>
        {
            ["title"] = title,
            ["sectionId"] = sectionId,
            ["gradeId"] = 5,
            ["weekNo"] = weekNo,
        };

    // ===== CRUD + validation + sanitize =====

    [Fact]
    public async Task CreateDoc_Valid_Persists_AndSanitizesHtml()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Tạo");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");

        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
            new
            {
                title = "Kế hoạch bài dạy Toán 5 tuần 1",
                sectionId,
                gradeId = 5,
                summary = "Tóm tắt ngắn",
                descriptionHtml =
                    "<p>Nội dung <strong>quan trọng</strong></p>" +
                    "<script>alert(1)</script>" +
                    "<a href=\"https://example.com/vi\">link</a>" +
                    "<a href=\"javascript:alert(2)\">xấu</a>" +
                    "<img src=\"https://res.cloudinary.com/x.jpg\" alt=\"ảnh\">",
                allowGuestDownload = true,
            }, teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await JsonAsync(resp)).GetProperty("id").GetInt64();

        var detailResp = await Client.SendAsync(Req(HttpMethod.Get, $"/api/teacher/documents/{id}", teacher.Cookie));
        detailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await JsonAsync(detailResp);

        var html = detail.GetProperty("descriptionHtml").GetString()!;
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("javascript:");
        html.ShouldContain("https://example.com/vi");
        html.ShouldContain("<strong>");
        html.ShouldContain("res.cloudinary.com");
        detail.GetProperty("allowGuestDownload").GetBoolean().ShouldBeTrue();
        detail.GetProperty("scope").GetString().ShouldBe("Public");
        detail.GetProperty("publishMode").GetString().ShouldBe("Visible");
        detail.GetProperty("publishState").GetString().ShouldBe("Visible");
    }

    [Fact]
    public async Task CreateDoc_RequireWeekSection_MissingWeek_Returns422()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV BTCT");
        var sectionId = await SectionIdAsync("bai-tap-cuoi-tuan");

        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
            DocBody(sectionId, "BT cuối tuần thiếu tuần"), teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await JsonAsync(resp);
        body.GetProperty("errors").GetProperty("weekNo").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateDoc_ForeignFile_Returns422()
    {
        var owner = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV FileOwner");
        var other = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Lạ");
        var fileId = await CreateFileInDbAsync(owner.Id, 1);
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");

        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
            new { title = "Chộm file", sectionId, gradeId = 5, fileIds = new[] { fileId } }, other.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(resp)).GetProperty("errors").GetProperty("fileIds").GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateDoc_MoreThan10Files_Returns422()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV 11File");
        var ids = new List<long>();
        for (var i = 0; i < 11; i++)
            ids.Add(await CreateFileInDbAsync(teacher.Id, i));
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");

        var resp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
            new { title = "Nhiều file quá", sectionId, gradeId = 5, fileIds = ids }, teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(resp)).GetProperty("errors").GetProperty("fileIds").GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateDoc_StaleUpdatedAt_Returns409()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Sửa");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var createResp = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
            DocBody(sectionId, "Tài liệu sửa"), teacher.Cookie));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await JsonAsync(createResp)).GetProperty("id").GetInt64();

        var detail = await JsonAsync(await Client.SendAsync(
            Req(HttpMethod.Get, $"/api/teacher/documents/{id}", teacher.Cookie)));
        var stale = "2020-01-01T00:00:00.0000000Z";

        var resp = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/documents/{id}",
            new
            {
                title = "Sửa tiêu đề",
                sectionId,
                gradeId = 5,
                updatedAt = stale,
            }, teacher.Cookie));
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // updatedAt đúng → OK
        var ok = await Client.SendAsync(WriteReq(HttpMethod.Put, $"/api/teacher/documents/{id}",
            new
            {
                title = "Sửa tiêu đề",
                sectionId,
                gradeId = 5,
                updatedAt = detail.GetProperty("updatedAt").GetString(),
            }, teacher.Cookie));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(ok)).GetProperty("title").GetString().ShouldBe("Sửa tiêu đề");
    }

    // ===== Phạm vi xem (spec §4.3) — không thấy = 404 =====

    [Fact]
    public async Task TeamScopeDoc_GuestAndOutsider404_Members200()
    {
        var owner = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Tổ A");
        var lead = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "Tổ trưởng A");
        var outsider = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Tổ B");
        var teamId = await CreateTeamAsync(lead.Id, TeamRole.Lead);
        await using (var db = _app.CreateDb())
        {
            db.TeamMembers.Add(new TeamMember
            {
                TeamId = teamId,
                UserId = owner.Id,
                Role = TeamRole.Member,
                JoinedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var doc = await CreateDocInDbAsync("Hồ sơ tổ nội bộ", owner.Id, sectionId,
            ContentScope.Team, PublishMode.Visible, teamId: teamId);

        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}", outsider.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}", owner.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}", lead.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Không lộ trong danh sách công khai của khách lẫn GV ngoài tổ
        var guestList = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&pageSize=100")));
        CheckAnyItem(guestList, doc.Id).ShouldBeFalse();
        var outsiderList = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&pageSize=100", outsider.Cookie)));
        CheckAnyItem(outsiderList, doc.Id).ShouldBeFalse();
    }

    private static bool CheckAnyItem(JsonElement list, long docId)
    {
        foreach (var item in list.GetProperty("items").EnumerateArray())
            if (item.GetProperty("id").GetInt64() == docId)
                return true;
        return false;
    }

    [Fact]
    public async Task TeachersScopeDoc_GuestExcluded_AuthenticatedTeacherSees()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Teachers");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var doc = await CreateDocInDbAsync("Chỉ giáo viên xem", teacher.Id, sectionId,
            ContentScope.Teachers, PublishMode.Visible);

        // Khách: chi tiết 404 + không có trong danh sách
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var guestList = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&pageSize=100")));
        CheckAnyItem(guestList, doc.Id).ShouldBeFalse();

        // GV Active: thấy
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}", teacher.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var teacherList = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&pageSize=100", teacher.Cookie)));
        CheckAnyItem(teacherList, doc.Id).ShouldBeTrue();
    }

    // ===== Tìm kiếm không dấu (spec §5.1, decisions.md M3) =====

    [Fact]
    public async Task PublicSearch_WithoutDiaccrits_FindsVietnameseTitle()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Tìm");
        var sectionId = await SectionIdAsync("ke-hoach-chu-nhiem");
        var doc = await CreateDocInDbAsync("Kế hoạch chủ nhiệm lớp 5A", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Visible);

        var resp = await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&q=" + Uri.EscapeDataString("ke hoach chu nhiem")));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await JsonAsync(resp);
        CheckAnyItem(list, doc.Id).ShouldBeTrue();

        // Có dấu vẫn tìm được
        var resp2 = await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&q=" + Uri.EscapeDataString("kế hoạch chủ nhiệm")));
        (await JsonAsync(resp2)).GetProperty("total").GetInt32().ShouldBeGreaterThan(0);
    }

    // ===== Hẹn giờ (spec §4.4) =====

    [Fact]
    public async Task ScheduledDoc_BeforeInsideAfterWindow_Visibility()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Hẹn");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var now = DateTimeOffset.UtcNow;

        var open = await CreateDocInDbAsync("BTCT đang mở", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Scheduled, weekNo: 31, from: now.AddHours(-1), until: now.AddHours(1));
        var upcoming = await CreateDocInDbAsync("BTCT sắp mở", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Scheduled, weekNo: 31, from: now.AddHours(1));
        var closed = await CreateDocInDbAsync("BTCT đã đóng", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Scheduled, weekNo: 31, from: now.AddHours(-3), until: now.AddHours(-1));

        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{open.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{upcoming.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{closed.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Danh sách: chỉ hiện "đang mở"; trạng thái cho UI đúng
        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/public/items?kind=document&pageSize=100")));
        CheckAnyItem(list, open.Id).ShouldBeTrue();
        CheckAnyItem(list, upcoming.Id).ShouldBeFalse();
        CheckAnyItem(list, closed.Id).ShouldBeFalse();

        // Owner thấy cả ba trong bảng của mình, trạng thái tính đúng
        var my = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/teacher/documents?pageSize=100", teacher.Cookie)));
        var states = new Dictionary<long, string>();
        foreach (var row in my.GetProperty("items").EnumerateArray())
            states[row.GetProperty("id").GetInt64()] = row.GetProperty("publishState").GetString()!;
        states[open.Id].ShouldBe("ScheduledOpen");
        states[upcoming.Id].ShouldBe("ScheduledUpcoming");
        states[closed.Id].ShouldBe("Closed");
    }

    // ===== Hiện/Ẩn — quyền (spec §2.2): owner ∨ Lead|Deputy của tổ ∨ Admin =====

    [Fact]
    public async Task Publish_OtherTeacher404_LeadOfTeam200_HidesDoc()
    {
        var owner = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Owner");
        var lead = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "Tổ trưởng");
        var outsider = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV Ngoài");
        var teamId = await CreateTeamAsync(lead.Id, TeamRole.Lead);

        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var doc = await CreateDocInDbAsync("Tài liệu của tổ", owner.Id, sectionId,
            ContentScope.Public, PublishMode.Visible, teamId: teamId);

        // GV ngoài tổ → 404
        var stranger = await Client.SendAsync(WriteReq(HttpMethod.Patch,
            $"/api/teacher/documents/{doc.Id}/publish", new { mode = "Hidden" }, outsider.Cookie));
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tổ trưởng của tổ nội dung → ẩn được
        var byLead = await Client.SendAsync(WriteReq(HttpMethod.Patch,
            $"/api/teacher/documents/{doc.Id}/publish", new { mode = "Hidden" }, lead.Cookie));
        byLead.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Hẹn giờ của owner → hiện lại trong khung
        var sched = await Client.SendAsync(WriteReq(HttpMethod.Patch,
            $"/api/teacher/documents/{doc.Id}/publish", new
            {
                mode = "Scheduled",
                from = DateTimeOffset.UtcNow.AddMinutes(-5).ToUniversalTime().ToString("o"),
                until = DateTimeOffset.UtcNow.AddHours(2).ToUniversalTime().ToString("o"),
            }, owner.Cookie));
        sched.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{doc.Id}"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Hẹn giờ không mốc → 422
        var bad = await Client.SendAsync(WriteReq(HttpMethod.Patch,
            $"/api/teacher/documents/{doc.Id}/publish", new { mode = "Scheduled" }, owner.Cookie));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Yêu thích (spec §5.2) =====

    [Fact]
    public async Task Favorites_AddListRemove_InvisibleItem404()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV YêuThích");
        var other = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV ChủẨn");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var visible = await CreateDocInDbAsync("Đáng lưu", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Visible);
        // Tài liệu ẩn của GV khác: với "teacher" nó thực sự vô hình (owner luôn thấy của mình, spec §4.3)
        var hidden = await CreateDocInDbAsync("Đang ẩn", other.Id, sectionId,
            ContentScope.Public, PublishMode.Hidden);

        // Lưu tài liệu đang hiện → 204
        var add = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/favorites",
            new { itemType = "document", itemId = visible.Id }, teacher.Cookie));
        add.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Tài liệu ẩn → 404 (không lộ tồn tại)
        var addHidden = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/favorites",
            new { itemType = "document", itemId = hidden.Id }, teacher.Cookie));
        addHidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/teacher/favorites", teacher.Cookie)));
        var found = false;
        foreach (var f in list.EnumerateArray())
            if (f.GetProperty("itemId").GetInt64() == visible.Id)
                found = true;
        found.ShouldBeTrue();

        var del = await Client.SendAsync(WriteReq(HttpMethod.Delete,
            $"/api/teacher/favorites/document/{visible.Id}", null, teacher.Cookie));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var list2 = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get,
            "/api/teacher/favorites", teacher.Cookie)));
        list2.GetArrayLength().ShouldBe(0);
    }

    // ===== Báo lỗi nội dung (spec §5.1) =====

    [Fact]
    public async Task Reports_Visible204_WithIpHash_Invisible404_MissingReason422()
    {
        var teacher = await CreateTeacherAsync($"t-{NewTag()}@gmail.com", "GV BáoLỗi");
        var sectionId = await SectionIdAsync("ke-hoach-bai-day");
        var visible = await CreateDocInDbAsync("Nội dung báo lỗi", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Visible);
        var hidden = await CreateDocInDbAsync("Nội dung ẩn", teacher.Id, sectionId,
            ContentScope.Public, PublishMode.Hidden);

        // Khách báo được tài liệu đang hiện (204) + lưu IP hash
        var ok = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/public/reports",
            new { itemType = "document", itemId = visible.Id, reason = "Lỗi chính tả", detail = "trang 2" }));
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var db = _app.CreateDb())
        {
            var report = await db.ContentReports.AsNoTracking()
                .SingleAsync(r => r.ItemId == visible.Id);
            report.ReporterIpHash.ShouldNotBeNullOrEmpty();
            report.Reason.ShouldBe("Lỗi chính tả");
        }

        // Nội dung ẩn → 404
        var nf = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/public/reports",
            new { itemType = "document", itemId = hidden.Id, reason = "Lỗi" }));
        nf.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Thiếu lý do → 422
        var bad = await Client.SendAsync(WriteReq(HttpMethod.Post, "/api/public/reports",
            new { itemType = "document", itemId = visible.Id }));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Kiểm duyệt (spec §4.5) =====

    [Fact]
    public async Task RequireReview_PublicDocGoesPendingReview_NotInPublicList()
    {
        if (!_app.DockerAvailable)
            throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");

        // Bật kiểm duyệt TRƯỚC khi tạo factory (cache setting đọc lần đầu)
        var hadOriginal = false;
        var originalValue = "";
        await using (var db = _app.CreateDb())
        {
            var existing = await db.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == SettingKeys.ContentRequireReview);
            hadOriginal = existing is not null;
            originalValue = existing?.Value ?? "";
            if (existing is not null)
            {
                db.AppSettings.Update(existing);
                existing.Value = "true";
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = SettingKeys.ContentRequireReview,
                    Value = "true",
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            var factory = _app.CreateFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            var tag = NewTag();
            long userId;
            await using (var db = _app.CreateDb())
            {
                var user = new User
                {
                    Email = $"t-{tag}@gmail.com",
                    GoogleSub = $"sub-{tag}",
                    FullName = "GV KiểmDuyệt",
                    SystemRole = SystemRole.Teacher,
                    Status = UserStatus.Active,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                userId = user.Id;
            }
            _app.FakeGoogle.Identities["tok-" + tag] =
                new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV KiểmDuyệt", null, null);
            factory.FakeGoogle.Identities["tok-" + tag] =
                new GoogleIdentity("sub-" + tag, $"t-{tag}@gmail.com", true, "GV KiểmDuyệt", null, null);
            var cookie = SessionCookieValue(await PostLoginAsync(client, "tok-" + tag))!;

            var sectionId = await SectionIdAsync("ke-hoach-bai-day");
            var resp = await client.SendAsync(WriteReq(HttpMethod.Post, "/api/teacher/documents",
                DocBody(sectionId, "Tài liệu chờ duyệt"), cookie));
            resp.StatusCode.ShouldBe(HttpStatusCode.Created);
            var id = (await JsonAsync(resp)).GetProperty("id").GetInt64();

            await using (var db = _app.CreateDb())
            {
                (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id))
                    .ModerationStatus.ShouldBe(ModerationStatus.PendingReview);
            }

            // Khách không thấy (chưa duyệt)
            (await client.SendAsync(Req(HttpMethod.Get, $"/api/public/documents/{id}"))).StatusCode
                .ShouldBe(HttpStatusCode.NotFound);

            // Owner vẫn thấy + trạng thái PendingReview
            var detail = await JsonAsync(await client.SendAsync(
                Req(HttpMethod.Get, $"/api/teacher/documents/{id}", cookie)));
            detail.GetProperty("moderationStatus").GetString().ShouldBe("PendingReview");
        }
        finally
        {
            await using var db = _app.CreateDb();
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.ContentRequireReview);
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
}
