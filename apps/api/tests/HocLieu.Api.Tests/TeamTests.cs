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
/// Nghiệm thu M2 (spec §2, §3.3, §5.3, §16): tổ & lời mời —
/// "Tổ trưởng tổ A gọi duyệt user xin vào tổ B → 403; mời email chưa có → đăng nhập bằng email đó → Active + vào tổ;
/// email khác → bị từ chối; một tổ không thể có 2 tổ trưởng".
///
/// Mọi request xác thực dùng client KHÔNG cookie container (cookie cầm tay) → không lẫn phiên
/// giữa các user trong cùng test (TestApp.Client có cookie jar chung, không dùng ở đây).
/// </summary>
public class TeamTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private ApiFactory? _factory;
    private Lazy<System.Net.Http.HttpClient>? _anonClient;

    public TeamTests(TestApp app) => _app = app;

    private void EnsureFactory() => _factory ??= _app.CreateFactory();

    /// <summary>
    /// Client KHÔNG cookie container: mọi request xác thực mang cookie cầm tay qua header
    /// → không lẫn phiên giữa các user trong cùng test (TestApp.Client có cookie jar chung, không dùng ở đây).
    /// Factory phụ cùng DB: cookie được DataProtection giải mã chéo qua PostgreSQL (đã chứng minh ở M1).
    /// </summary>
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

    /// <summary>Tạo user (DB) + fake Google identity (cả 2 factory), đăng nhập, trả Actor có cookie phiên.</summary>
    private async Task<Actor> CreateTeacherAsync(
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

    /// <summary>Identity giả phải có mặt ở CẢ factory chính lẫn factory phụ (test đăng nhập qua client phụ).</summary>
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

    private async Task AddMemberAsync(long teamId, long userId, TeamRole role)
    {
        await using var db = _app.CreateDb();
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = teamId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>GV Pending đang xin vào tổ (đã điền requested_team_id qua luồng /cho-duyet).</summary>
    private async Task<long> CreatePendingApplicantAsync(string email, string name, long requestedTeamId)
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

    /// <summary>Đăng nhập (lần đầu hoặc lần sau) — trả cookie phiên mới nhất.</summary>
    private async Task<string> LoginAsync(string token, string? inviteToken = null)
    {
        var cookie = SessionCookieValue(await PostLoginAsync(Client, token, inviteToken));
        cookie.ShouldNotBeNullOrEmpty();
        return cookie!;
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

    private static string TokenFromLink(string link)
    {
        var marker = "/moi/";
        var at = link.IndexOf(marker, StringComparison.Ordinal);
        return at >= 0 ? link[(at + marker.Length)..] : link;
    }

    // ===== Phân quyền xem tổ =====

    [Fact]
    public async Task TeamDetail_NonMember_Returns404()
    {
        var stranger = await CreateTeacherAsync($"stranger-{NewTag()}@gmail.com", "GV Lạ");
        var teamA = await CreateTeamAsync("Tổ " + NewTag());

        var response = await Client.SendAsync(Req(HttpMethod.Get, $"/api/teams/{teamA}", stranger.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response)).ShouldBe("not_found");
    }

    [Fact]
    public async Task TeamDetail_Member_SeesRoleCountsAndLead()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lead-{tag}@gmail.com", "Tổ Trưởng X");
        var member = await CreateTeacherAsync($"member-{tag}@gmail.com", "Thành Viên X");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);

        var response = await Client.SendAsync(Req(HttpMethod.Get, $"/api/teams/{teamA}", member.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);

        json.GetProperty("name").GetString().ShouldBe("Tổ " + tag);
        json.GetProperty("memberCount").GetInt32().ShouldBe(2);
        json.GetProperty("leadName").GetString().ShouldBe("Tổ Trưởng X");
        json.GetProperty("role").GetString().ShouldBe("Member");
        json.GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task TeamDetail_Unauthenticated_Returns401()
    {
        var teamA = await CreateTeamAsync("Tổ " + NewTag());
        var response = await Client.GetAsync($"/api/teams/{teamA}");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ===== Hàng chờ duyệt — phân quyền (nghiệm thu M2) =====

    [Fact]
    public async Task LeadOfTeamA_ApproveRequestForTeamB_Returns403()
    {
        // "Tổ trưởng tổ A gọi duyệt user xin vào tổ B → 403"
        var tag = NewTag();
        var leadA = await CreateTeacherAsync($"lead-a-{tag}@gmail.com", "Trưởng A");
        var teamA = await CreateTeamAsync("Tổ A " + tag);
        var teamB = await CreateTeamAsync("Tổ B " + tag);
        await AddMemberAsync(teamA, leadA.Id, TeamRole.Lead);
        var applicant = await CreatePendingApplicantAsync($"applicant-{tag}@gmail.com", "Xin Vào B", teamB);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamB}/join-requests/{applicant}/approve", leadA.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).ShouldBe("team.not_manage");

        // user vẫn Pending — không bị ảnh hưởng
        await using (var db = _app.CreateDb())
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == applicant);
            user.Status.ShouldBe(UserStatus.Pending);
        }
    }

    [Fact]
    public async Task PlainMember_ApproveRequest_Returns403()
    {
        var tag = NewTag();
        var member = await CreateTeacherAsync($"pm-{tag}@gmail.com", "Thành Viên Thường");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);
        var applicant = await CreatePendingApplicantAsync($"ap-{tag}@gmail.com", "Xin Vào", teamA);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/join-requests/{applicant}/approve", member.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).ShouldBe("team.not_manage");
    }

    // ===== Duyệt / từ chối =====

    [Fact]
    public async Task Lead_ApproveJoinRequest_UserActiveMemberNotifiedAudited()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"ld-{tag}@gmail.com", "Trưởng Duyệt");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var applicant = await CreatePendingApplicantAsync($"duyet-{tag}@gmail.com", "GV Được Duyệt", teamA);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/join-requests/{applicant}/approve", lead.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("ok").GetBoolean().ShouldBeTrue();

        await using (var db = _app.CreateDb())
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == applicant);
            user.Status.ShouldBe(UserStatus.Active);
            user.ApprovedBy.ShouldBe(lead.Id);
            user.RequestedTeamId.ShouldBeNull();
            (await db.TeamMembers.AsNoTracking().AnyAsync(m => m.TeamId == teamA && m.UserId == applicant
                    && m.Role == TeamRole.Member)).ShouldBeTrue();
            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "team.join_request.approved"
                    && a.ActorUserId == lead.Id)).ShouldBeTrue();
            (await db.Notifications.AsNoTracking().AnyAsync(n => n.UserId == applicant
                    && n.Type == "team.approved")).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Lead_RejectJoinRequest_UserRejectedWithReason()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lj-{tag}@gmail.com", "Trưởng Từ Chối");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var applicant = await CreatePendingApplicantAsync($"tc-{tag}@gmail.com", "GV Bị Từ Chối", teamA);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/join-requests/{applicant}/reject", lead.Cookie,
            JsonSerializer.Serialize(new { reason = "Thiếu thông tin" })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var db = _app.CreateDb())
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == applicant);
            user.Status.ShouldBe(UserStatus.Rejected);
            user.StatusReason.ShouldBe("Thiếu thông tin");
            user.RequestedTeamId.ShouldBeNull();
            (await db.Notifications.AsNoTracking().AnyAsync(n => n.UserId == applicant
                    && n.Type == "team.rejected")).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task BulkApprove_TwoPendingUsers_BothApproved()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lb-{tag}@gmail.com", "Trưởng Hàng Loạt");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var a1 = await CreatePendingApplicantAsync($"b1-{tag}@gmail.com", "GV 1", teamA);
        var a2 = await CreatePendingApplicantAsync($"b2-{tag}@gmail.com", "GV 2", teamA);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/join-requests/approve", lead.Cookie,
            JsonSerializer.Serialize(new { userIds = new[] { a1, a2 } })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await JsonAsync(response);
        results.GetArrayLength().ShouldBe(2);
        results[0].GetProperty("ok").GetBoolean().ShouldBeTrue();
        results[1].GetProperty("ok").GetBoolean().ShouldBeTrue();

        await using (var db = _app.CreateDb())
        {
            var ids = new[] { a1, a2 };
            (await db.Users.AsNoTracking().CountAsync(u => ids.Contains(u.Id)
                    && u.Status == UserStatus.Active)).ShouldBe(2);
        }
    }

    // ===== Lời mời (nghiệm thu M2) =====

    [Fact]
    public async Task Invite_NewEmail_TokenLogin_BecomesActiveInTeam()
    {
        // "mời email chưa có → đăng nhập bằng email đó → Active + vào tổ"
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"li-{tag}@gmail.com", "Trưởng Mời");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var inviteEmail = $"moinhat-{tag}@gmail.com";

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { inviteEmail }, message = "Chào bạn!" })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await JsonAsync(response);
        results.GetArrayLength().ShouldBe(1);
        var result = results[0];
        result.GetProperty("action").GetString().ShouldBe("invited");
        result.GetProperty("link").GetString()!.ShouldContain("/moi/");

        var token = TokenFromLink(result.GetProperty("link").GetString()!);
        await using (var db = _app.CreateDb())
        {
            var invite = await db.Invitations.AsNoTracking().FirstAsync(i => i.Email == inviteEmail);
            // hết hạn 7 ngày (±1 ngày cho sai số)
            (invite.ExpiresAt - DateTimeOffset.UtcNow).TotalDays.ShouldBeInRange(6, 8);
        }

        // đăng nhập bằng chính email được mời + token
        AddFake("tok-inv-" + tag, $"sub-inv-{tag}", inviteEmail, "GV Mới");
        var cookie = await LoginAsync("tok-inv-" + tag, token);
        var me = (JsonElement)JsonSerializer.Deserialize(
            await (await Client.SendAsync(Req(HttpMethod.Get, "/api/me", cookie))).Content.ReadAsStringAsync(),
            typeof(JsonElement))!;
        me.GetProperty("status").GetString().ShouldBe("Active");
        var teams = me.GetProperty("teams");
        teams.GetArrayLength().ShouldBe(1);
        teams[0].GetProperty("id").GetInt64().ShouldBe(teamA);
        teams[0].GetProperty("role").GetString().ShouldBe("Member");

        await using (var db = _app.CreateDb())
        {
            var invite = await db.Invitations.AsNoTracking().FirstAsync(i => i.Email == inviteEmail);
            invite.AcceptedAt.ShouldNotBe(null);
        }
    }

    [Fact]
    public async Task Invite_TokenWithDifferentEmail_Returns400()
    {
        // "email khác → bị từ chối"
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lm-{tag}@gmail.com", "Trưởng Mời 2");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var inviteEmail = $"duocmoi-{tag}@gmail.com";

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { inviteEmail } })));
        var token = TokenFromLink(((await JsonAsync(response)))[0].GetProperty("link").GetString()!);

        AddFake("tok-mm-" + tag, $"sub-mm-{tag}", $"nguokhac-{tag}@gmail.com", "Người Khác");
        var loginResponse = await PostLoginAsync(Client, "tok-mm-" + tag, token);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(loginResponse)).ShouldBe("invite.email_mismatch");
    }

    [Fact]
    public async Task Invite_ExistingActiveUser_AddedDirectly()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lx-{tag}@gmail.com", "Trưởng Mời 3");
        var existing = await CreateTeacherAsync($"tonthat-{tag}@gmail.com", "GV Đã Có");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { existing.Email } })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await JsonAsync(response))[0];
        result.GetProperty("action").GetString().ShouldBe("added");
        result.GetProperty("invitationId").ValueKind.ShouldBe(JsonValueKind.Null);

        await using (var db = _app.CreateDb())
        {
            (await db.TeamMembers.AsNoTracking().AnyAsync(m => m.TeamId == teamA && m.UserId == existing.Id
                    && m.Role == TeamRole.Member)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Invite_Resend_NewTokenWorks_OldTokenDead()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lr-{tag}@gmail.com", "Trưởng Gửi Lại");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var inviteEmail = $"resend-{tag}@gmail.com";

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { inviteEmail } })));
        var results = await JsonAsync(response);
        var invId = results[0].GetProperty("invitationId").GetGuid();
        var oldToken = TokenFromLink(results[0].GetProperty("link").GetString()!);

        var resend = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations/{invId}/resend", lead.Cookie));
        resend.StatusCode.ShouldBe(HttpStatusCode.OK);
        // resend trả 1 object (không phải list)
        var newToken = TokenFromLink((await JsonAsync(resend)).GetProperty("link").GetString()!);
        newToken.ShouldNotBe(oldToken);

        AddFake("tok-rs-" + tag, $"sub-rs-{tag}", inviteEmail, "GV Resend");
        // token cũ → không hợp lệ (hash đã đổi)
        (await PostLoginAsync(Client, "tok-rs-" + tag, oldToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // token mới → Active + vào tổ
        var cookie = await LoginAsync("tok-rs-" + tag, newToken);
        var me = (JsonElement)JsonSerializer.Deserialize(
            await (await Client.SendAsync(Req(HttpMethod.Get, "/api/me", cookie))).Content.ReadAsStringAsync(),
            typeof(JsonElement))!;
        me.GetProperty("status").GetString().ShouldBe("Active");
        me.GetProperty("teams")[0].GetProperty("id").GetInt64().ShouldBe(teamA);
    }

    [Fact]
    public async Task Invite_Revoke_TokenLoginFails()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lk-{tag}@gmail.com", "Trưởng Thu Hồi");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var inviteEmail = $"revoke-{tag}@gmail.com";

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { inviteEmail } })));
        var results = await JsonAsync(response);
        var invId = results[0].GetProperty("invitationId").GetGuid();
        var token = TokenFromLink(results[0].GetProperty("link").GetString()!);

        (await Client.SendAsync(Req(HttpMethod.Delete, $"/api/teams/{teamA}/invitations/{invId}", lead.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        AddFake("tok-rv-" + tag, $"sub-rv-{tag}", inviteEmail, "GV Revoke");
        var login = await PostLoginAsync(Client, "tok-rv-" + tag, token);
        login.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(login)).ShouldBe("invite.invalid");

        // thu hồi lần 2 → 400
        var again = await Client.SendAsync(Req(HttpMethod.Delete, $"/api/teams/{teamA}/invitations/{invId}", lead.Cookie));
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(again)).ShouldBe("invite.revoked");
    }

    // ===== Vai trò thành viên (đặt/bỏ tổ phó, gỡ) =====

    [Fact]
    public async Task SetMemberRole_LeadSetsDeputy()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"sl-{tag}@gmail.com", "Trưởng Đặt Phó");
        var member = await CreateTeacherAsync($"sm-{tag}@gmail.com", "GV Được Bổ Nhiệm");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);

        var response = await Client.SendAsync(Req(
            HttpMethod.Patch, $"/api/teams/{teamA}/members/{member.Id}", lead.Cookie,
            JsonSerializer.Serialize(new { role = "Deputy" })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("role").GetString().ShouldBe("Deputy");

        await using (var db = _app.CreateDb())
        {
            var m = await db.TeamMembers.AsNoTracking().FirstAsync(x => x.TeamId == teamA && x.UserId == member.Id);
            m.Role.ShouldBe(TeamRole.Deputy);
        }
    }

    [Fact]
    public async Task SetMemberRole_NonLead_Returns403()
    {
        var tag = NewTag();
        var deputy = await CreateTeacherAsync($"dp-{tag}@gmail.com", "Tổ Phó");
        var plain = await CreateTeacherAsync($"pl-{tag}@gmail.com", "Thành Viên Thường");
        var target = await CreateTeacherAsync($"tg-{tag}@gmail.com", "Mục Tiêu");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, deputy.Id, TeamRole.Deputy);
        await AddMemberAsync(teamA, plain.Id, TeamRole.Member);
        await AddMemberAsync(teamA, target.Id, TeamRole.Member);

        // tổ phó không được đặt vai trò (chỉ Lead | Admin — spec §2.2)
        var asDeputy = await Client.SendAsync(Req(
            HttpMethod.Patch, $"/api/teams/{teamA}/members/{target.Id}", deputy.Cookie,
            JsonSerializer.Serialize(new { role = "Deputy" })));
        asDeputy.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(asDeputy)).ShouldBe("team.not_lead");

        // thành viên thường cũng không
        var asPlain = await Client.SendAsync(Req(
            HttpMethod.Patch, $"/api/teams/{teamA}/members/{target.Id}", plain.Cookie,
            JsonSerializer.Serialize(new { role = "Deputy" })));
        asPlain.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(asPlain)).ShouldBe("team.not_lead");
    }

    [Fact]
    public async Task SetMemberRole_ToLeadTarget_Returns400()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"lt-{tag}@gmail.com", "Trưởng Tự Đổi");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);

        // Lead không đổi được vai trò chính mình qua endpoint thành viên (chỉ qua bổ nhiệm Admin)
        var response = await Client.SendAsync(Req(
            HttpMethod.Patch, $"/api/teams/{teamA}/members/{lead.Id}", lead.Cookie,
            JsonSerializer.Serialize(new { role = "Member" })));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).ShouldBe("team.not_lead");
    }

    [Fact]
    public async Task RemoveMember_LeadCanRemoveMember_CannotRemoveLead()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"rm-{tag}@gmail.com", "Trưởng Gỡ");
        var member = await CreateTeacherAsync($"rm-m-{tag}@gmail.com", "GV Bị Gỡ");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);

        var removed = await Client.SendAsync(Req(
            HttpMethod.Delete, $"/api/teams/{teamA}/members/{member.Id}", lead.Cookie));
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using (var db = _app.CreateDb())
        {
            (await db.TeamMembers.AsNoTracking().AnyAsync(m => m.TeamId == teamA && m.UserId == member.Id))
                .ShouldBeFalse();
        }

        // gỡ Lead → 400
        var self = await Client.SendAsync(Req(
            HttpMethod.Delete, $"/api/teams/{teamA}/members/{lead.Id}", lead.Cookie));
        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(self)).ShouldBe("team.not_lead");
    }

    // ===== Admin: tổ, bổ nhiệm tổ trưởng, lời mời toàn hệ thống =====

    [Fact]
    public async Task AdminSetLead_OldLeadDemoted_OneLeadOnly()
    {
        // "một tổ không thể có 2 tổ trưởng"
        var tag = NewTag();
        var admin = await CreateTeacherAsync($"adm-{tag}@gmail.com", "Quản Trị Viên", role: SystemRole.Admin);
        var oldLead = await CreateTeacherAsync($"ol-{tag}@gmail.com", "Trưởng Cũ");
        var newLead = await CreateTeacherAsync($"nl-{tag}@gmail.com", "Trưởng Mới");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, oldLead.Id, TeamRole.Lead);

        var response = await Client.SendAsync(Req(
            HttpMethod.Put, $"/api/admin/teams/{teamA}/lead", admin.Cookie,
            JsonSerializer.Serialize(new { userId = newLead.Id })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("leadName").GetString().ShouldBe("Trưởng Mới");

        await using (var db = _app.CreateDb())
        {
            var leads = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == teamA && m.Role == TeamRole.Lead)
                .Select(m => m.UserId)
                .ToListAsync();
            leads.ShouldBe(new[] { newLead.Id });
            var old = await db.TeamMembers.AsNoTracking().FirstAsync(m => m.UserId == oldLead.Id);
            old.Role.ShouldBe(TeamRole.Member);
        }
    }

    [Fact]
    public async Task AdminSetLead_NonActiveUser_Returns422()
    {
        var tag = NewTag();
        var admin = await CreateTeacherAsync($"adm2-{tag}@gmail.com", "Admin 2", role: SystemRole.Admin);
        var pending = await CreateTeacherAsync($"pend-{tag}@gmail.com", "GV Chờ", UserStatus.Pending);
        var teamA = await CreateTeamAsync("Tổ " + tag);

        var response = await Client.SendAsync(Req(
            HttpMethod.Put, $"/api/admin/teams/{teamA}/lead", admin.Cookie,
            JsonSerializer.Serialize(new { userId = pending.Id })));
        response.StatusCode.ShouldBe((HttpStatusCode)422);
    }

    [Fact]
    public async Task AdminTeams_CreateDuplicateUpdateDeactivate()
    {
        var tag = NewTag();
        var admin = await CreateTeacherAsync($"adm3-{tag}@gmail.com", "Admin 3", role: SystemRole.Admin);
        var name = "Tổ Admin " + tag;

        var created = await Client.SendAsync(Req(
            HttpMethod.Post, "/api/admin/teams", admin.Cookie,
            JsonSerializer.Serialize(new { name, description = "Mô tả", gradeId = (short)3 })));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var teamId = (await JsonAsync(created)).GetProperty("id").GetInt64();

        var dup = await Client.SendAsync(Req(
            HttpMethod.Post, "/api/admin/teams", admin.Cookie,
            JsonSerializer.Serialize(new { name })));
        dup.StatusCode.ShouldBe((HttpStatusCode)409);

        var updated = await Client.SendAsync(Req(
            HttpMethod.Put, $"/api/admin/teams/{teamId}", admin.Cookie,
            JsonSerializer.Serialize(new { name = name + " Sửa" })));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(updated)).GetProperty("name").GetString().ShouldBe(name + " Sửa");

        // GV thường gọi API admin → 403
        var teacher = await CreateTeacherAsync($"t403-{tag}@gmail.com", "GV Thường");
        (await Client.SendAsync(Req(HttpMethod.Get, "/api/admin/teams", teacher.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // dừng hoạt động → thành viên không mời được nữa
        var lead = await CreateTeacherAsync($"dl-{tag}@gmail.com", "Trưởng Dừng");
        await AddMemberAsync(teamId, lead.Id, TeamRole.Lead);
        (await Client.SendAsync(Req(HttpMethod.Delete, $"/api/admin/teams/{teamId}", admin.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var invite = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamId}/invitations", lead.Cookie,
            JsonSerializer.Serialize(new { emails = new[] { $"x-{tag}@gmail.com" } })));
        invite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invite)).ShouldBe("team.inactive");
    }

    [Fact]
    public async Task AdminInvite_LeadRole_SwapsExistingLead()
    {
        // lời mời vai trò Lead được chấp nhận → Lead cũ hạ xuống Member (unique index 1 Lead/tổ)
        var tag = NewTag();
        var admin = await CreateTeacherAsync($"adm4-{tag}@gmail.com", "Admin 4", role: SystemRole.Admin);
        var oldLead = await CreateTeacherAsync($"ol2-{tag}@gmail.com", "Trưởng Cũ 2");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, oldLead.Id, TeamRole.Lead);
        var inviteEmail = $"leadmoi-{tag}@gmail.com";

        var response = await Client.SendAsync(Req(
            HttpMethod.Post, "/api/admin/invitations", admin.Cookie,
            JsonSerializer.Serialize(new { teamId = teamA, teamRole = "Lead", emails = new[] { inviteEmail } })));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await JsonAsync(response);
        results[0].GetProperty("action").GetString().ShouldBe("invited");
        var token = TokenFromLink(results[0].GetProperty("link").GetString()!);

        AddFake("tok-ls-" + tag, $"sub-ls-{tag}", inviteEmail, "Tổ Trưởng Mới");
        var cookie = await LoginAsync("tok-ls-" + tag, token);
        var me = (JsonElement)JsonSerializer.Deserialize(
            await (await Client.SendAsync(Req(HttpMethod.Get, "/api/me", cookie))).Content.ReadAsStringAsync(),
            typeof(JsonElement))!;
        me.GetProperty("status").GetString().ShouldBe("Active");
        me.GetProperty("teams")[0].GetProperty("role").GetString().ShouldBe("Lead");

        await using (var db = _app.CreateDb())
        {
            var old = await db.TeamMembers.AsNoTracking().FirstAsync(m => m.UserId == oldLead.Id);
            old.Role.ShouldBe(TeamRole.Member);
            (await db.TeamMembers.AsNoTracking().CountAsync(m => m.TeamId == teamA && m.Role == TeamRole.Lead))
                .ShouldBe(1);
        }
    }

    // ===== Thông báo tổ =====

    [Fact]
    public async Task TeamAnnouncement_LeadPosts_MemberSees_NonMember404()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"ta-{tag}@gmail.com", "Trưởng Thông Báo");
        var member = await CreateTeacherAsync($"ta-m-{tag}@gmail.com", "GV Đọc Báo");
        var stranger = await CreateTeacherAsync($"ta-s-{tag}@gmail.com", "Người Ngoài");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);

        var posted = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/announcements", lead.Cookie,
            JsonSerializer.Serialize(new
            {
                title = "Họp tổ cuối tuần",
                bodyHtml = "<p>Họp <strong>17:00</strong> thứ 6</p>",
                isPinned = true,
            })));
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);

        var listed = await Client.SendAsync(Req(HttpMethod.Get, $"/api/teams/{teamA}/announcements", member.Cookie));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = await JsonAsync(listed);
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("Họp tổ cuối tuần");
        items[0].GetProperty("bodyHtml").GetString()!.ShouldContain("<strong>17:00</strong>");
        items[0].GetProperty("isPinned").GetBoolean().ShouldBeTrue();

        // thông báo in-app cho thành viên
        await using (var db = _app.CreateDb())
        {
            (await db.Notifications.AsNoTracking().AnyAsync(n => n.UserId == member.Id
                    && n.Type == "team.announcement")).ShouldBeTrue();
        }

        // người ngoài tổ → 404
        (await Client.SendAsync(Req(HttpMethod.Get, $"/api/teams/{teamA}/announcements", stranger.Cookie)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // thành viên thường không đăng được → 403
        var forbidden = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/announcements", member.Cookie,
            JsonSerializer.Serialize(new { title = "Không được đăng" })));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ===== Thống kê tổ =====

    [Fact]
    public async Task TeamStats_MemberSeesCounts()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"st-{tag}@gmail.com", "Trưởng Thống Kê");
        var member = await CreateTeacherAsync($"st-m-{tag}@gmail.com", "GV Thống Kê");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        await AddMemberAsync(teamA, member.Id, TeamRole.Member);

        var response = await Client.SendAsync(Req(HttpMethod.Get, $"/api/teams/{teamA}/stats", member.Cookie));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(response);
        json.GetProperty("memberCount").GetInt32().ShouldBe(2);
        json.GetProperty("members").GetArrayLength().ShouldBe(2);
        json.GetProperty("members")[0].GetProperty("role").GetString().ShouldBe("Lead");
    }

    // ===== Thông báo in-app =====

    [Fact]
    public async Task Notifications_ListAndMarkRead()
    {
        var tag = NewTag();
        var lead = await CreateTeacherAsync($"nt-{tag}@gmail.com", "Trưởng Thông Báo 2");
        var teamA = await CreateTeamAsync("Tổ " + tag);
        await AddMemberAsync(teamA, lead.Id, TeamRole.Lead);
        var applicantEmail = $"nt-a-{tag}@gmail.com";
        var applicant = await CreatePendingApplicantAsync(applicantEmail, "GV Chờ Duyệt 2", teamA);

        var approved = await Client.SendAsync(Req(
            HttpMethod.Post, $"/api/teams/{teamA}/join-requests/{applicant}/approve", lead.Cookie));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);

        // user vừa được duyệt đăng nhập → xem thông báo
        AddFake("tok-nt-" + tag, $"sub-nt-{tag}", applicantEmail, "GV Chờ Duyệt 2");
        var cookie = await LoginAsync("tok-nt-" + tag);

        var list = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/me/notifications", cookie)));
        list.GetProperty("items").GetArrayLength().ShouldBe(1);
        list.GetProperty("unreadCount").GetInt32().ShouldBe(1);
        var notifId = list.GetProperty("items")[0].GetProperty("id").GetInt64();
        notifId.ShouldNotBe(0);
        list.GetProperty("items")[0].GetProperty("type").GetString().ShouldBe("team.approved");

        var read = await Client.SendAsync(Req(HttpMethod.Post, "/api/me/notifications/read", cookie));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(read)).GetProperty("updated").GetInt32().ShouldBe(1);

        var after = await JsonAsync(await Client.SendAsync(Req(HttpMethod.Get, "/api/me/notifications", cookie)));
        after.GetProperty("unreadCount").GetInt32().ShouldBe(0);
        after.GetProperty("items")[0].GetProperty("read").GetBoolean().ShouldBeTrue();
    }
}
