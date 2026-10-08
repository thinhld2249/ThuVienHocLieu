using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M6 (spec §5.4, §16) — Admin hoàn thiện:
/// "Không khóa được admin cuối cùng; chuyển nội dung GV A → B đổi owner toàn bộ, ghi audit;
/// kết chuyển năm: lớp 4A → 5A năm mới kèm HS, lớp 5 cũ lưu trữ" + dashboard, nội dung,
/// báo cáo, danh mục, lớp, thông báo/trang tĩnh, cài đặt, nhật ký, hệ thống.
///
/// Mọi request xác thực dùng client KHÔNG cookie container (cookie cầm tay) — như TeamTests.
/// </summary>
public class AdminTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public AdminTests(TestApp app) => _app = app;

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

    private sealed record Actor(long Id, string Email, string Name, string Token, string Cookie);

    private static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Actor> CreateActorAsync(
        string email, string name, UserStatus status = UserStatus.Active, SystemRole role = SystemRole.Teacher)
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
                SystemRole = role,
                Status = status,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        }
        AddFake("tok-" + tag, $"sub-{tag}", email, name);
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-" + tag));
        cookie.ShouldNotBeNullOrEmpty();
        return new Actor(id, email, name, "tok-" + tag, cookie!);
    }

    /// <summary>Identity giả phải có mặt ở CẢ factory chính lẫn factory phụ.</summary>
    private void AddFake(string token, string sub, string email, string name)
    {
        _app.FakeGoogle.Identities[token] = new GoogleIdentity(sub, email, true, name, null, null);
        EnsureFactory();
        _factory!.FakeGoogle.Identities[token] = new GoogleIdentity(sub, email, true, name, null, null);
    }

    private async Task<long> CreateTeamAsync(string name)
    {
        await using var db = _app.CreateDb();
        var team = new Team { Name = name };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return team.Id;
    }

    /// <summary>GV Pending đang xin vào tổ (luồng /cho-duyet).</summary>
    private async Task<long> CreatePendingApplicantAsync(string email, string name, long? requestedTeamId)
    {
        var tag = NewTag();
        await using var db = _app.CreateDb();
        var user = new User
        {
            Email = email,
            GoogleSub = $"sub-{tag}",
            FullName = name,
            Status = UserStatus.Pending,
            RequestedTeamId = requestedTeamId,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(Document Doc, Quiz Quiz)> CreateContentAsync(long ownerId)
    {
        var tag = NewTag();
        await using var db = _app.CreateDb();
        var doc = new Document
        {
            Title = "Tài liệu " + tag,
            Slug = $"tl-{tag}",
            Scope = ContentScope.Private,
            OwnerId = ownerId,
        };
        var quiz = new Quiz
        {
            Title = "Bài tập " + tag,
            Slug = $"bt-{tag}",
            OwnerId = ownerId,
        };
        db.Documents.Add(doc);
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();
        return (doc, quiz);
    }

    private static Task<HttpResponseMessage> PostLoginAsync(
        System.Net.Http.HttpClient client, string token, string? inviteToken = null)
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["idToken"] = token,
                ["inviteToken"] = inviteToken,
            }),
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

    private static HttpRequestMessage Req(HttpMethod method, string url, string cookie, string? bodyJson = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", $"hl_session={cookie}");
        req.Headers.Add("X-Requested-With", "hoclieu");
        if (bodyJson is not null)
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        return req;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => (JsonElement)JsonSerializer.Deserialize(
            await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => (await JsonAsync(response)).GetProperty("code").GetString() ?? "";

    /// <summary>Một id từ mảng items (tìm theo trường "id").</summary>
    private static bool ItemsContainId(JsonElement json, long id)
        => json.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetInt64() == id);

    // ===== Phân quyền =====

    [Fact]
    public async Task AdminEndpoints_Teacher_Returns403()
    {
        var teacher = await CreateActorAsync($"teacher-{NewTag()}@gmail.com", "GV Thường");

        var response = await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/dashboard", teacher.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ===== Dashboard =====

    [Fact]
    public async Task Dashboard_ReturnsUserCountsApprovalQueueAndStorage()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin X", role: SystemRole.Admin);
        var team = await CreateTeamAsync("Tổ " + tag);
        var pendingId = await CreatePendingApplicantAsync($"pending-{tag}@gmail.com", "GV Chờ Duyệt", team);

        var response = await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/dashboard", admin.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);

        json.GetProperty("users").GetProperty("pending").GetInt32().ShouldBe(1);
        json.GetProperty("users").GetProperty("active").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        var queue = json.GetProperty("approvalQueue");
        queue.EnumerateArray().Any(u => u.GetProperty("userId").GetInt64() == pendingId).ShouldBeTrue();
        json.GetProperty("teamsWithoutLead").EnumerateArray().Any(t => t.GetString() == "Tổ " + tag).ShouldBeTrue();
        json.GetProperty("contentNew").GetProperty("documents30d").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        json.GetProperty("attempts").GetProperty("last7d").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        json.GetProperty("openReports").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        json.GetProperty("storage").GetProperty("totalBytes").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
        json.GetProperty("failedFiles").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // ===== Người dùng (§16: "Không khóa được admin cuối cùng") =====

    [Fact]
    public async Task UsersList_FiltersByStatus()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin Y", role: SystemRole.Admin);
        var pendingId = await CreatePendingApplicantAsync($"pending2-{tag}@gmail.com", "GV Chờ Duyệt 2", null);

        var response = await Client.SendAsync(
            Req(HttpMethod.Get, $"/api/admin/users?status=Pending&pageSize=100", admin.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);

        json.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.All(u => u.GetProperty("status").GetString() == "Pending").ShouldBeTrue();
        ItemsContainId(json, pendingId).ShouldBeTrue();
    }

    [Fact]
    public async Task PatchUser_ApprovePendingIntoTeam()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin Z", role: SystemRole.Admin);
        var team = await CreateTeamAsync("Tổ " + tag);
        var pendingId = await CreatePendingApplicantAsync($"pending3-{tag}@gmail.com", "GV Xin Vào", team);

        var response = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/users/{pendingId}", admin.Cookie,
            JsonSerializer.Serialize(new { status = "Active", teamIds = new long[] { team } })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);
        json.GetProperty("status").GetString().ShouldBe("Active");
        json.GetProperty("teams").EnumerateArray().Any(t =>
            t.GetProperty("teamId").GetInt64() == team
            && t.GetProperty("role").GetString() == "Member").ShouldBeTrue();

        // DB: Active + thành viên tổ thật sự
        await using var db = _app.CreateDb();
        var user = await db.Users.FirstAsync(u => u.Id == pendingId);
        user.Status.ShouldBe(UserStatus.Active);
        (await db.TeamMembers.AnyAsync(m => m.UserId == pendingId && m.TeamId == team)).ShouldBeTrue();
    }

    [Fact]
    public async Task PatchUser_CannotSuspendOrDemoteLastActiveAdmin()
    {
        // "Không khóa được admin cuối cùng" — 409 last_admin, DB không đổi
        var tag = NewTag();
        var onlyAdmin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin Cuối Cùng", role: SystemRole.Admin);
        await CreateActorAsync($"teacher-{tag}@gmail.com", "GV Khác"); // để chắc chắn có user khác

        // Các test trong class dùng chung DB — hạ quyền mọi Admin Active khác để chỉ còn 1
        await using (var db0 = _app.CreateDb())
        {
            var others = await db0.Users
                .Where(u => u.SystemRole == SystemRole.Admin && u.Status == UserStatus.Active && u.Id != onlyAdmin.Id)
                .ToListAsync();
            foreach (var u in others)
                u.SystemRole = SystemRole.Teacher;
            await db0.SaveChangesAsync();
        }

        var r1 = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/users/{onlyAdmin.Id}", onlyAdmin.Cookie,
            JsonSerializer.Serialize(new { status = "Suspended" })));
        r1.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(r1)).ShouldBe("last_admin");

        var r2 = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/users/{onlyAdmin.Id}", onlyAdmin.Cookie,
            JsonSerializer.Serialize(new { systemRole = "Teacher" })));
        r2.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(r2)).ShouldBe("last_admin");

        await using var db = _app.CreateDb();
        var user = await db.Users.FirstAsync(u => u.Id == onlyAdmin.Id);
        user.Status.ShouldBe(UserStatus.Active);
        user.SystemRole.ShouldBe(SystemRole.Admin);
    }

    [Fact]
    public async Task PatchUser_CanDemoteAdmin_WhenAnotherActiveAdminExists()
    {
        var tag = NewTag();
        var admin1 = await CreateActorAsync($"admin1-{tag}@gmail.com", "Admin Một", role: SystemRole.Admin);
        var admin2 = await CreateActorAsync($"admin2-{tag}@gmail.com", "Admin Hai", role: SystemRole.Admin);

        var response = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/users/{admin1.Id}", admin2.Cookie,
            JsonSerializer.Serialize(new { systemRole = "Teacher" })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("systemRole").GetString().ShouldBe("Teacher");
    }

    // ===== Chuyển quyền sở hữu (§16: "chuyển nội dung GV A → B đổi owner toàn bộ, ghi audit") =====

    [Fact]
    public async Task TransferContent_MovesAllDocumentsAndQuizzes_AndAudits()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin T", role: SystemRole.Admin);
        var a = await CreateActorAsync($"gv-a-{tag}@gmail.com", "GV A");
        var b = await CreateActorAsync($"gv-b-{tag}@gmail.com", "GV B");
        var (doc, quiz) = await CreateContentAsync(a.Id);

        var response = await Client.SendAsync(
            Req(HttpMethod.Post, $"/api/admin/users/{a.Id}/transfer-content", admin.Cookie,
                JsonSerializer.Serialize(new { toUserId = b.Id })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);
        json.GetProperty("documents").GetInt32().ShouldBe(1);
        json.GetProperty("quizzes").GetInt32().ShouldBe(1);

        // DB: owner đổi toàn bộ (kể cả phạm vi Private)
        await using var db = _app.CreateDb();
        var d = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == doc.Id);
        var q = await db.Quizzes.AsNoTracking().FirstAsync(x => x.Id == quiz.Id);
        d.OwnerId.ShouldBe(b.Id);
        q.OwnerId.ShouldBe(b.Id);

        // Audit log ghi lại hành động
        var audit = await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/audit-logs?action=admin.user.transfer_content", admin.Cookie));
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auditJson = await JsonAsync(audit);
        auditJson.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        auditJson.GetProperty("items").EnumerateArray().Any(x =>
            x.GetProperty("action").GetString() == "admin.user.transfer_content"
            && x.GetProperty("entityId").GetString() == a.Id.ToString()).ShouldBeTrue();
    }

    [Fact]
    public async Task LogoutAll_InvalidatesOldSession()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin L", role: SystemRole.Admin);
        var teacher = await CreateActorAsync($"teacher-{tag}@gmail.com", "GV Cần Đăng Xuất");

        var response = await Client.SendAsync(
            Req(HttpMethod.Post, $"/api/admin/users/{teacher.Id}/logout-all", admin.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Phiên cũ của GV phải mất hiệu lực ngay
        var me = await Client.SendAsync(Req(HttpMethod.Get, "/api/me", teacher.Cookie));
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ===== Nội dung (/admin/noi-dung) =====

    [Fact]
    public async Task Content_AdminSeesPrivateContent_DeleteRestore_ScheduleAndFeature()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin C", role: SystemRole.Admin);
        var owner = await CreateActorAsync($"gv-{tag}@gmail.com", "GV Chủ");
        var (doc, quiz) = await CreateContentAsync(owner.Id);

        // Danh sách document — tài liệu Private vẫn hiện cho Admin (khác khu công khai)
        var list = await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/content?kind=document", admin.Cookie));
        var listBody = await list.Content.ReadAsStringAsync();
        list.StatusCode.ShouldBe(HttpStatusCode.OK, listBody);
        var listJson = JsonDocument.Parse(listBody).RootElement;
        ItemsContainId(listJson, doc.Id).ShouldBeTrue();
        var docItem = listJson.GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("id").GetInt64() == doc.Id);
        docItem.GetProperty("scope").GetString().ShouldBe("Private");
        docItem.GetProperty("ownerName").GetString().ShouldBe("GV Chủ");

        // Danh sách quiz
        var quizList = await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/content?kind=quiz", admin.Cookie));
        ItemsContainId(await JsonAsync(quizList), quiz.Id).ShouldBeTrue();

        // Hẹn giờ rồi ẩn → xóa mốc (quy tắc §4.4)
        var sched = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/content/document/{doc.Id}", admin.Cookie,
            JsonSerializer.Serialize(new
            {
                publishMode = "Scheduled",
                publishFrom = "2030-06-05T10:00:00Z",
                publishUntil = "2030-06-07T12:00:00Z",
            })));
        sched.StatusCode.ShouldBe(HttpStatusCode.OK);

        var hide = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/content/document/{doc.Id}", admin.Cookie,
            JsonSerializer.Serialize(new { publishMode = "Hidden", isFeatured = true })));
        hide.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var db = _app.CreateDb())
        {
            var d = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == doc.Id);
            d.PublishMode.ShouldBe(PublishMode.Hidden);
            d.PublishFrom.ShouldBeNull(); // ẩn → xóa lịch
            d.PublishUntil.ShouldBeNull();
            d.IsFeatured.ShouldBeTrue();
        }

        // Xóa mềm → mất khỏi danh sách mặc định, hiện ở deleted=true, rồi khôi phục
        var del = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/content/document/{doc.Id}", admin.Cookie,
            JsonSerializer.Serialize(new { isDeleted = true })));
        del.StatusCode.ShouldBe(HttpStatusCode.OK);

        var defaultList = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/content?kind=document", admin.Cookie)));
        ItemsContainId(defaultList, doc.Id).ShouldBeFalse();

        var deletedList = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/content?kind=document&deleted=true", admin.Cookie)));
        ItemsContainId(deletedList, doc.Id).ShouldBeTrue();

        var restore = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/content/document/{doc.Id}", admin.Cookie,
            JsonSerializer.Serialize(new { isDeleted = false })));
        restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/content?kind=document", admin.Cookie)));
        ItemsContainId(restored, doc.Id).ShouldBeTrue();
    }

    // ===== Báo cáo (/admin/bao-cao) =====

    [Fact]
    public async Task Reports_Resolve_SetsStatusNoteAndAuditor()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin R", role: SystemRole.Admin);
        var owner = await CreateActorAsync($"gv-{tag}@gmail.com", "GV Báo Cáo");
        var (doc, _) = await CreateContentAsync(owner.Id);

        long reportId;
        await using (var db = _app.CreateDb())
        {
            db.ContentReports.Add(new ContentReport
            {
                ItemType = FavoriteItemType.Document,
                ItemId = doc.Id,
                Reason = "Sai nội dung",
                Status = ReportStatus.Open,
            });
            await db.SaveChangesAsync();
            reportId = db.ContentReports.Local.First(r => r.ItemId == doc.Id).Id;
        }

        var list = await JsonAsync(await Client.SendAsync(
            Req(HttpMethod.Get, "/api/admin/reports?status=Open", admin.Cookie)));
        var item = list.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetInt64() == reportId);
        item.GetProperty("itemTitle").GetString().ShouldBe(doc.Title);
        item.GetProperty("status").GetString().ShouldBe("Open");

        var patch = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/reports/{reportId}", admin.Cookie,
            JsonSerializer.Serialize(new { status = "Resolved", note = "Đã kiểm tra, nội dung đúng" })));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(patch)).GetProperty("status").GetString().ShouldBe("Resolved");

        await using var db2 = _app.CreateDb();
        var report = await db2.ContentReports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        report.Status.ShouldBe(ReportStatus.Resolved);
        report.Note.ShouldBe("Đã kiểm tra, nội dung đúng");
        report.ResolvedBy.ShouldBe(admin.Id);
        report.ResolvedAt.ShouldNotBeNull();

        // status không hợp lệ → 422
        var bad = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/reports/{reportId}", admin.Cookie,
            JsonSerializer.Serialize(new { status = "Open" })));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Danh mục (/admin/danh-muc) =====

    [Fact]
    public async Task Taxonomy_SectionCrud_SoftDeactivate()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin S", role: SystemRole.Admin);
        var name = "Đề CKPT " + tag;

        var created = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/sections", admin.Cookie,
            JsonSerializer.Serialize(new { name, contentKind = "Quiz", defaultPublishMode = "Hidden" }))));
        created.GetProperty("name").GetString().ShouldBe(name);
        created.GetProperty("contentKind").GetString().ShouldBe("Quiz");
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        var sectionId = created.GetProperty("id").GetInt64();

        // Trùng slug → 409
        var dup = await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/sections", admin.Cookie,
            JsonSerializer.Serialize(new { name })));
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var updated = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Put, $"/api/admin/sections/{sectionId}", admin.Cookie,
            JsonSerializer.Serialize(new { name = name + " (sửa)", isActive = false }))));
        updated.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        var del = await Client.SendAsync(Req(HttpMethod.Delete, $"/api/admin/sections/{sectionId}", admin.Cookie));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Mềm: vẫn còn trong danh sách nhưng đã tắt
        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/sections", admin.Cookie)));
        var s = list.EnumerateArray().First(x => x.GetProperty("id").GetInt64() == sectionId);
        s.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Taxonomy_SubjectsTags_SchoolYears()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin M", role: SystemRole.Admin);

        // Môn học: tạo → tắt
        var subj = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/subjects", admin.Cookie,
            JsonSerializer.Serialize(new { name = "Lĩnh vực " + tag }))));
        var subjectId = subj.GetProperty("id").GetInt64();
        subj.GetProperty("slug").GetString().ShouldNotBeEmpty();
        (await Client.SendAsync(Req(HttpMethod.Delete, $"/api/admin/subjects/{subjectId}", admin.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var subjects = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/subjects", admin.Cookie)));
        subjects.EnumerateArray().First(x => x.GetProperty("id").GetInt64() == subjectId)
            .GetProperty("isActive").GetBoolean().ShouldBeFalse();

        // Tag: tạo → sửa → xóa cứng
        var t = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/tags", admin.Cookie,
            JsonSerializer.Serialize(new { name = "tag " + tag }))));
        var tagId = t.GetProperty("id").GetInt64();
        await Client.SendAsync(Req(HttpMethod.Put, $"/api/admin/tags/{tagId}", admin.Cookie,
            JsonSerializer.Serialize(new { name = "tag " + tag + " x" })));
        (await Client.SendAsync(Req(HttpMethod.Delete, $"/api/admin/tags/{tagId}", admin.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var tags = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/tags", admin.Cookie)));
        tags.EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == tagId).ShouldBeFalse();

        // Năm học: tạo → sửa
        var year = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/school-years", admin.Cookie,
            JsonSerializer.Serialize(new { name = "2098-2099", startDate = "2098-09-05", endDate = "2099-05-31" }))));
        var yearId = year.GetProperty("id").GetInt64();
        year.GetProperty("isCurrent").GetBoolean().ShouldBeFalse();
        var yearUpdated = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Put, $"/api/admin/school-years/{yearId}", admin.Cookie,
            JsonSerializer.Serialize(new { name = "2098-2099", startDate = "2098-09-01", endDate = "2099-05-31" }))));
        yearUpdated.GetProperty("startDate").GetString().ShouldBe("2098-09-01");
        ErrorCodeSafe(yearUpdated).ShouldBeEmpty();

        // Ngày hết hạn trước ngày bắt đầu → 422
        var bad = await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/school-years", admin.Cookie,
            JsonSerializer.Serialize(new { name = "2099-2101", startDate = "2101-01-01", endDate = "2099-01-01" })));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static string ErrorCodeSafe(JsonElement json)
    {
        try { return json.GetProperty("code").GetString() ?? ""; }
        catch (KeyNotFoundException) { return ""; }
    }

    // ===== Kết chuyển năm học (§16: "lớp 4A → 5A năm mới kèm HS, lớp 5 cũ lưu trữ") =====

    [Fact]
    public async Task Rollover_ArchivesOldYearClonesToNextGrade_KeepsGrade5OnlyArchived()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin N", role: SystemRole.Admin);
        var homeroom = await CreateActorAsync($"cn-{tag}@gmail.com", "GV CN");
        var subjectTeacher = await CreateActorAsync($"bm-{tag}@gmail.com", "GV BM");

        long currentYearId, class4AId, class5BId;
        await using (var db = _app.CreateDb())
        {
            currentYearId = await db.SchoolYears.Where(y => y.IsCurrent).Select(y => y.Id).FirstAsync();
            var cls4A = new ClassEntity
            {
                Name = "4A-" + tag,
                GradeId = 4,
                SchoolYearId = currentYearId,
                HomeroomTeacherId = homeroom.Id,
                IsArchived = false,
            };
            cls4A.Students.Add(new Student { Ordinal = 1, FullName = "HS Một " + tag });
            cls4A.Students.Add(new Student { Ordinal = 2, FullName = "HS Hai " + tag });
            cls4A.Teachers.Add(new ClassTeacher { UserId = subjectTeacher.Id });
            var cls5B = new ClassEntity
            {
                Name = "5B-" + tag,
                GradeId = 5,
                SchoolYearId = currentYearId,
                IsArchived = false,
            };
            cls5B.Students.Add(new Student { Ordinal = 1, FullName = "HS Ba " + tag });
            db.Classes.Add(cls4A);
            db.Classes.Add(cls5B);
            await db.SaveChangesAsync();
            class4AId = cls4A.Id;
            class5BId = cls5B.Id;
        }

        // Tạo năm mới rồi kết chuyển
        var yearResp = await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/school-years", admin.Cookie,
            JsonSerializer.Serialize(new { name = "2097-2098", startDate = "2097-09-05", endDate = "2098-05-31" })));
        yearResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var newYearId = (await JsonAsync(yearResp)).GetProperty("id").GetInt64();

        var rollover = await Client.SendAsync(Req(HttpMethod.Post, $"/api/admin/school-years/{newYearId}/rollover", admin.Cookie,
            JsonSerializer.Serialize(new { cloneClassesToNextGrade = true })));
        rollover.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await JsonAsync(rollover);
        result.GetProperty("oldYearId").GetInt64().ShouldBe(currentYearId);
        result.GetProperty("newYearId").GetInt64().ShouldBe(newYearId);
        // "≥": test trong class dùng chung DB — test khác có thể đã tạo lớp trong năm hiện tại
        result.GetProperty("archivedClasses").GetInt32().ShouldBeGreaterThanOrEqualTo(2); // ≥ 4A + 5B
        result.GetProperty("clonedClasses").GetInt32().ShouldBeGreaterThanOrEqualTo(1);   // ≥ 4A (khối 5 không nhân bản)
        result.GetProperty("clonedStudents").GetInt32().ShouldBeGreaterThanOrEqualTo(2);  // ≥ 2 HS của 4A

        // DB: cờ current đảo; lớp cũ lưu trữ; lớp mới đúng khối +1 kèm HS + GV bộ môn
        await using var db2 = _app.CreateDb();
        var oldYear = await db2.SchoolYears.AsNoTracking().FirstAsync(y => y.Id == currentYearId);
        var newYear = await db2.SchoolYears.AsNoTracking().FirstAsync(y => y.Id == newYearId);
        oldYear.IsCurrent.ShouldBeFalse();
        newYear.IsCurrent.ShouldBeTrue();

        var old4A = await db2.Classes.AsNoTracking().FirstAsync(c => c.Id == class4AId);
        var old5B = await db2.Classes.AsNoTracking().FirstAsync(c => c.Id == class5BId);
        old4A.IsArchived.ShouldBeTrue();
        old5B.IsArchived.ShouldBeTrue();

        var new4A = await db2.Classes.AsNoTracking()
            .Include(c => c.Students).Include(c => c.Teachers)
            .FirstAsync(c => c.SchoolYearId == newYearId && c.Name == "4A-" + tag);
        new4A.GradeId.ShouldBe((short)5);
        new4A.IsArchived.ShouldBeFalse();
        new4A.HomeroomTeacherId.ShouldBe(homeroom.Id);
        new4A.Students.Count(s => s.IsActive).ShouldBe(2);
        new4A.Teachers.Count(t => t.UserId == subjectTeacher.Id).ShouldBe(1);

        (await db2.Classes.AsNoTracking().AnyAsync(c =>
            c.SchoolYearId == newYearId && c.Name == "5B-" + tag)).ShouldBeFalse();
    }

    [Fact]
    public async Task Rollover_AlreadyCurrentYear_Returns422()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin N2", role: SystemRole.Admin);

        await using var db = _app.CreateDb();
        var currentId = await db.SchoolYears.Where(y => y.IsCurrent).Select(y => y.Id).FirstAsync();

        var response = await Client.SendAsync(Req(HttpMethod.Post, $"/api/admin/school-years/{currentId}/rollover",
            admin.Cookie, JsonSerializer.Serialize(new { cloneClassesToNextGrade = false })));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Lớp (/admin/lop) =====

    [Fact]
    public async Task Classes_ListAndChangeHomeroom()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin CL", role: SystemRole.Admin);
        var newTeacher = await CreateActorAsync($"cnmoi-{tag}@gmail.com", "GV CN Mới");
        var pendingTeacher = await CreateActorAsync($"cn-{tag}@gmail.com", "GV Chờ Duyệt", UserStatus.Pending);

        long classId, currentYearId;
        await using (var db = _app.CreateDb())
        {
            currentYearId = await db.SchoolYears.Where(y => y.IsCurrent).Select(y => y.Id).FirstAsync();
            var cls = new ClassEntity { Name = "2C-" + tag, GradeId = 2, SchoolYearId = currentYearId, IsArchived = false };
            cls.Students.Add(new Student { Ordinal = 1, FullName = "HS A " + tag });
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
            classId = cls.Id;
        }

        var list = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Get, $"/api/admin/classes?q=2C-{tag}", admin.Cookie)));
        var item = list.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetInt64() == classId);
        item.GetProperty("activeStudentCount").GetInt32().ShouldBe(1);
        item.GetProperty("gradeId").GetInt16().ShouldBe((short)2);

        // GV chưa Active làm chủ nhiệm → 422
        var bad = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/classes/{classId}", admin.Cookie,
            JsonSerializer.Serialize(new { homeroomTeacherId = pendingTeacher.Id })));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var patch = await Client.SendAsync(Req(HttpMethod.Patch, $"/api/admin/classes/{classId}", admin.Cookie,
            JsonSerializer.Serialize(new { homeroomTeacherId = newTeacher.Id })));
        patch.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db2 = _app.CreateDb();
        var cls2 = await db2.Classes.AsNoTracking().FirstAsync(c => c.Id == classId);
        cls2.HomeroomTeacherId.ShouldBe(newTeacher.Id);
    }

    // ===== Thông báo & trang tĩnh (/admin/thong-bao) =====

    [Fact]
    public async Task Announcements_Crud_AndSanitized()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin TB", role: SystemRole.Admin);

        // Team mà không có teamId → 422
        var bad = await Client.SendAsync(Req(HttpMethod.Post, "/api/admin/announcements", admin.Cookie,
            JsonSerializer.Serialize(new { title = "TB " + tag, audience = "Team" })));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var createBody = JsonSerializer.Serialize(new
        {
            title = "Thông báo " + tag,
            bodyHtml = "<p><script>alert(1)</script>Nội dung an toàn</p>",
            audience = "Teachers",
        });
        var created = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Post, "/api/admin/announcements", admin.Cookie, createBody)));
        var id = created.GetProperty("id").GetInt64();
        created.GetProperty("bodyHtml").GetString()!.ShouldNotContain("<script>");

        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/announcements", admin.Cookie)));
        var item = list.EnumerateArray().First(x => x.GetProperty("id").GetInt64() == id);
        item.GetProperty("audience").GetString().ShouldBe("Teachers");

        var updated = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Put, $"/api/admin/announcements/{id}", admin.Cookie,
            JsonSerializer.Serialize(new
            {
                title = "Thông báo " + tag + " (sửa)",
                bodyHtml = "<p>Mới</p>",
                audience = "Public",
                isPinned = true,
            }))));
        updated.GetProperty("title").GetString().ShouldBe("Thông báo " + tag + " (sửa)");
        updated.GetProperty("isPinned").GetBoolean().ShouldBeTrue();

        (await Client.SendAsync(Req(HttpMethod.Delete, $"/api/admin/announcements/{id}", admin.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var list2 = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/announcements", admin.Cookie)));
        list2.EnumerateArray().Any(x => x.GetProperty("id").GetInt64() == id).ShouldBeFalse();
    }

    [Fact]
    public async Task StaticPages_UpsertAndList()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin P", role: SystemRole.Admin);

        var put = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Put, "/api/admin/pages/gioi-thieu", admin.Cookie,
            JsonSerializer.Serialize(new { title = "Giới thiệu cổng " + tag, bodyMarkdown = "# Xin chào" }))));
        put.GetProperty("slug").GetString().ShouldBe("gioi-thieu");
        put.GetProperty("title").GetString().ShouldBe("Giới thiệu cổng " + tag);

        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/pages", admin.Cookie)));
        var page = list.EnumerateArray().First(x => x.GetProperty("slug").GetString() == "gioi-thieu");
        page.GetProperty("bodyMarkdown").GetString().ShouldBe("# Xin chào");
    }

    // ===== Cài đặt (/admin/cai-dat) =====

    [Fact]
    public async Task Settings_PutReflectsInGet_UnknownKeyRejected()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin SD", role: SystemRole.Admin);
        var siteName = "Cổng " + tag;

        var put = await Client.SendAsync(Req(HttpMethod.Put, "/api/admin/settings", admin.Cookie,
            JsonSerializer.Serialize(new { settings = new Dictionary<string, object?> { ["site.name"] = siteName } })));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var get = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/settings", admin.Cookie)));
        get.GetProperty("settings").GetProperty("site.name").GetString().ShouldBe(siteName);
        // Các key còn lại vẫn trả về giá trị mặc định hợp lệ
        get.GetProperty("settings").GetProperty("upload.max_mb").GetInt32().ShouldBe(50);

        var bad = await Client.SendAsync(Req(HttpMethod.Put, "/api/admin/settings", admin.Cookie,
            JsonSerializer.Serialize(new { settings = new Dictionary<string, object?> { ["site.evil"] = "x" } })));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ===== Nhật ký (/admin/nhat-ky) =====

    [Fact]
    public async Task AuditLogs_FilterByAction_AndCsvExport()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin AK", role: SystemRole.Admin);

        // Sinh một entry audit xác định
        await Client.SendAsync(Req(HttpMethod.Put, "/api/admin/settings", admin.Cookie,
            JsonSerializer.Serialize(new { settings = new Dictionary<string, object?> { ["site.contact"] = new { email = "x@" + tag + ".vn" } } })));

        var list = await JsonAsync(await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/audit-logs?action=admin.settings.update", admin.Cookie)));
        list.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        list.GetProperty("items").EnumerateArray().ToList().All(x =>
            x.GetProperty("action").GetString() == "admin.settings.update").ShouldBeTrue();

        var csv = await Client.SendAsync(Req(
            HttpMethod.Get, "/api/admin/audit-logs.csv?action=admin.settings.update", admin.Cookie));
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType?.MediaType.ShouldBe("text/csv");
        var text = await csv.Content.ReadAsStringAsync();
        text.ShouldContain("Id;Người dùng;Hành động;Đối tượng;Mã đối tượng;Dữ liệu;IP;Thời gian");
        text.ShouldContain("admin.settings.update");
    }

    // ===== Hệ thống (/admin/he-thong) =====

    [Fact]
    public async Task System_HealthAndFileRetry()
    {
        var tag = NewTag();
        var admin = await CreateActorAsync($"admin-{tag}@gmail.com", "Admin HT", role: SystemRole.Admin);

        var response = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/system", admin.Cookie)));
        response.GetProperty("health").GetProperty("db").GetString().ShouldBe("Healthy");
        response.GetProperty("files").GetProperty("pending").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        response.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();

        // File lỗi → retry → Pending; file Ready → 422
        long failedId, readyId;
        await using (var db = _app.CreateDb())
        {
            db.Files.Add(new FileEntity
            {
                OriginalName = "file-loi.docx",
                Ext = "docx",
                Mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Bytes = 100,
                Sha256 = new string('a', 64),
                StoragePublicId = $"tests/{tag}/loi",
                ProcessingStatus = ProcessingStatus.Failed,
                ProcessingError = "Lỗi giả lập",
                ProcessingAttempts = 3,
            });
            db.Files.Add(new FileEntity
            {
                OriginalName = "file-sẵn.png",
                Ext = "png",
                Mime = "image/png",
                Bytes = 100,
                Sha256 = new string('b', 64),
                StoragePublicId = $"tests/{tag}/san",
                ProcessingStatus = ProcessingStatus.Ready,
            });
            await db.SaveChangesAsync();
            failedId = db.Files.Local.First(f => f.ProcessingStatus == ProcessingStatus.Failed).Id;
            readyId = db.Files.Local.First(f => f.ProcessingStatus == ProcessingStatus.Ready).Id;
        }

        var retry = await Client.SendAsync(Req(HttpMethod.Post, $"/api/admin/system/files/{failedId}/retry", admin.Cookie));
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(retry)).GetProperty("status").GetString().ShouldBe("Pending");

        var notAllowed = await Client.SendAsync(Req(HttpMethod.Post, $"/api/admin/system/files/{readyId}/retry", admin.Cookie));
        notAllowed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
