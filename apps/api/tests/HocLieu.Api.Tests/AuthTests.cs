using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using HocLieu.Common.Auth;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M1 (spec §3, §16): đăng nhập Google qua FakeGoogleTokenValidator,
/// vòng đời tài khoản, security_stamp, phiên sống qua "restart" (DataProtection keys trong PostgreSQL).
/// </summary>
public class AuthTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public AuthTests(TestApp app) => _app = app;

    private System.Net.Http.HttpClient Client
    {
        get
        {
            if (!_app.DockerAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");
            return _app.Client;
        }
    }

    // ===== Helpers =====

    private async Task<JsonElement> LoginAsync(System.Net.Http.HttpClient client, string token, string? inviteToken = null)
    {
        var response = await PostLoginAsync(client, token, inviteToken);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"login {token} → {(int)response.StatusCode}: {text}");
        return (JsonElement)JsonSerializer.Deserialize(text, typeof(JsonElement))!;
    }

    private async Task<HttpResponseMessage> PostLoginAsync(System.Net.Http.HttpClient client, string token, string? inviteToken = null)
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
        return await client.PostAsync("/api/auth/google", body);
    }

    private static string? SessionCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null; // response không cấp cookie (vd 403)
        foreach (var raw in values)
        {
            var cookie = raw.Split(';', 2)[0];
            if (cookie.StartsWith("hl_session=", StringComparison.OrdinalIgnoreCase))
                return cookie["hl_session=".Length..];
        }
        return null;
    }

    private static HttpRequestMessage Authed(string url, string cookieValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"hl_session={cookieValue}");
        return req;
    }

    private static void AddFake(TestApp app, string token, string email, string name = "GV Test")
        => app.FakeGoogle.Identities[token] = new GoogleIdentity($"sub-{token}", email, true, name, null, null);

    // ===== §3.1: đăng nhập & trạng thái user mới =====

    [Fact]
    public async Task NewUser_Login_CreatesPendingUserWithSession()
    {
        AddFake(_app, "tok-new-1", "gioviennu@gmail.com", "GV Mới");
        var me = await LoginAsync(Client, "tok-new-1");

        me.GetProperty("status").GetString().ShouldBe("Pending");
        me.GetProperty("systemRole").GetString().ShouldBe("Teacher");
        me.GetProperty("email").GetString().ShouldBe("gioviennu@gmail.com");
        me.GetProperty("teams").GetArrayLength().ShouldBe(0);

        // GET /api/me bằng cookie phiên
        (await Client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminEmail_Login_BecomesAdminActive()
    {
        var factory = _app.CreateFactory(new Dictionary<string, string?>
        {
            ["Auth:AdminEmails"] = "admin@test.edu",
        });
        factory.FakeGoogle.Identities["tok-admin"] =
            new GoogleIdentity("sub-admin", "admin@test.edu", true, "Quản Trị", null, null);
        var client = factory.CreateClient();

        var response = await PostLoginAsync(client, "tok-admin");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var me = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        me.GetProperty("status").GetString().ShouldBe("Active");
        me.GetProperty("systemRole").GetString().ShouldBe("Admin");
    }

    [Fact]
    public async Task AutoApproveDomain_Login_BecomesActiveWithoutTeam()
    {
        // setting auth.auto_approve_domains = ["duytot.edu.vn"] (upsert — seed đã có key này)
        await using (var db = _app.CreateDb())
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "auth.auto_approve_domains");
            if (setting is null)
            {
                db.AppSettings.Add(new AppSetting { Key = "auth.auto_approve_domains", Value = """["duytot.edu.vn"]""" });
            }
            else
            {
                setting.Value = """["duytot.edu.vn"]""";
            }
            await db.SaveChangesAsync();
        }

        var factory = _app.CreateFactory();
        factory.FakeGoogle.Identities["tok-domain"] =
            new GoogleIdentity("sub-domain", "gv@duytot.edu.vn", true, "GV Domain", null, null);
        var client = factory.CreateClient();

        var response = await PostLoginAsync(client, "tok-domain");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var me = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        me.GetProperty("status").GetString().ShouldBe("Active");
        me.GetProperty("teams").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task AllowedDomains_LoginFromOtherDomain_Returns403()
    {
        var factory = _app.CreateFactory(new Dictionary<string, string?>
        {
            ["Auth:AllowedDomains"] = "school.edu.vn",
        });
        factory.FakeGoogle.Identities["tok-out"] =
            new GoogleIdentity("sub-out", "dangnhap@gmail.com", true, "Ngoài Domain", null, null);
        var client = factory.CreateClient();

        var response = await PostLoginAsync(client, "tok-out");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("code").GetString().ShouldBe("auth.domain_not_allowed");
    }

    [Fact]
    public async Task SuspendedUser_Login_Returns403WithoutSession()
    {
        await using (var db = _app.CreateDb())
        {
            db.Users.Add(new User
            {
                Email = "khoa@gmail.com",
                GoogleSub = "sub-khoa",
                FullName = "GV Khóa",
                Status = UserStatus.Suspended,
                StatusReason = "Vi phạm nội quy",
            });
            await db.SaveChangesAsync();
        }
        _app.FakeGoogle.Identities["tok-khoa"] =
            new GoogleIdentity("sub-khoa", "khoa@gmail.com", true, "GV Khóa", null, null);

        var response = await PostLoginAsync(Client, "tok-khoa");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("code").GetString().ShouldBe("user.suspended");
        json.GetProperty("title").GetString()!.ShouldContain("Vi phạm nội quy");
        SessionCookieValue(response).ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task RejectedUser_Login_Returns403()
    {
        await using (var db = _app.CreateDb())
        {
            db.Users.Add(new User
            {
                Email = "tuchoi@gmail.com",
                GoogleSub = "sub-tuchoi",
                FullName = "GV Từ Chối",
                Status = UserStatus.Rejected,
                StatusReason = "Thiếu thông tin",
            });
            await db.SaveChangesAsync();
        }
        _app.FakeGoogle.Identities["tok-tuchoi"] =
            new GoogleIdentity("sub-tuchoi", "tuchoi@gmail.com", true, "GV Từ Chối", null, null);

        var response = await PostLoginAsync(Client, "tok-tuchoi");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("code").GetString().ShouldBe("user.rejected");
    }

    [Fact]
    public async Task Login_InvalidToken_Returns401()
    {
        var response = await PostLoginAsync(Client, "token-chung-khong-biet");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithoutCsrfHeader_Returns403()
    {
        AddFake(_app, "tok-csrf", "csrf@gmail.com");
        var body = new StringContent("""{"idToken":"tok-csrf"}""", Encoding.UTF8, "application/json");
        // cố ý KHÔNG kèm X-Requested-With
        var response = await Client.PostAsync("/api/auth/google", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("code").GetString().ShouldBe("csrf");
    }

    // ===== §3.4: security_stamp & phiên =====

    [Fact]
    public async Task LogoutAll_OldSessionBecomesInvalid()
    {
        AddFake(_app, "tok-stamp", "stamp@gmail.com");
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-stamp"));
        cookie.ShouldNotBeNullOrEmpty();

        var logoutAll = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout-all");
        logoutAll.Headers.Add("Cookie", $"hl_session={cookie}");
        logoutAll.Headers.Add("X-Requested-With", "hoclieu");
        (await Client.SendAsync(logoutAll)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // cookie cũ → 401 (stamp đã đổi + cache bị clear → đọc thẳng DB)
        (await Client.SendAsync(Authed("/api/me", cookie!))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Session_SurvivesRestart_DataProtectionKeysInDb()
    {
        AddFake(_app, "tok-restart", "restart@gmail.com");
        var cookie = SessionCookieValue(await PostLoginAsync(Client, "tok-restart"));
        cookie.ShouldNotBeNullOrEmpty();

        // "quá trình" mới (factory khác) cùng DB: cookie vẫn hợp lệ
        var response = await _app.Client2.SendAsync(Authed("/api/me", cookie!));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        me.GetProperty("email").GetString().ShouldBe("restart@gmail.com");
    }

    // ===== §3.1: PUT /api/me (lưu /cho-duyet) =====

    [Fact]
    public async Task PendingUser_UpdateMe_SetsRequestedTeam()
    {
        AddFake(_app, "tok-pending", "choduyet@gmail.com");
        await LoginAsync(Client, "tok-pending");

        long teamId;
        await using (var db = _app.CreateDb())
        {
            var team = new Team { Name = "Tô" + Guid.NewGuid().ToString("N")[..6] };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        var put = new HttpRequestMessage(HttpMethod.Put, "/api/me");
        put.Headers.Add("X-Requested-With", "hoclieu");
        put.Content = new StringContent(
            JsonSerializer.Serialize(new { fullName = "Trần Thị Chờ Duyệt", phone = "0912345678", requestedTeamId = teamId }),
            Encoding.UTF8,
            "application/json");
        var response = await Client.SendAsync(put);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var me = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        me.GetProperty("fullName").GetString().ShouldBe("Trần Thị Chờ Duyệt");
        me.GetProperty("phone").GetString().ShouldBe("0912345678");
        me.GetProperty("requestedTeamId").GetInt64().ShouldBe(teamId);
        me.GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task PendingUser_UpdateMe_BadPhone_Returns422()
    {
        AddFake(_app, "tok-phone", "phone@gmail.com");
        await LoginAsync(Client, "tok-phone");

        var put = new HttpRequestMessage(HttpMethod.Put, "/api/me");
        put.Headers.Add("X-Requested-With", "hoclieu");
        put.Content = new StringContent("""{"phone":"abc"}""", Encoding.UTF8, "application/json");
        (await Client.SendAsync(put)).StatusCode.ShouldBe((HttpStatusCode)422);
    }

    // ===== §3.3: lời mời (xử lý ở cổng đăng nhập; quản lý lời mời là M2) =====

    [Fact]
    public async Task Invitation_ValidToken_NewUserBecomesActiveInTeam()
    {
        long teamId;
        string token = "inv-" + Guid.NewGuid().ToString("N")[..10];
        await using (var db = _app.CreateDb())
        {
            var team = new Team { Name = "Tô" + Guid.NewGuid().ToString("N")[..6] };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            teamId = team.Id;

            db.Invitations.Add(new Invitation
            {
                Email = "moinhat@gmail.com",
                TeamId = teamId,
                TeamRole = TeamRole.Member,
                TokenHash = AuthService.HashToken(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        AddFake(_app, "tok-invite", "moinhat@gmail.com", "GV Được Mời");
        var me = await LoginAsync(Client, "tok-invite", token);

        me.GetProperty("status").GetString().ShouldBe("Active");
        var teams = me.GetProperty("teams");
        teams.GetArrayLength().ShouldBe(1);
        teams[0].GetProperty("id").GetInt64().ShouldBe(teamId);
        teams[0].GetProperty("role").GetString().ShouldBe("Member");

        await using (var db = _app.CreateDb())
        {
            var invite = await db.Invitations.FirstAsync(x => x.TokenHash == AuthService.HashToken(token));
            invite.AcceptedAt.ShouldNotBe(null);
        }
    }

    [Fact]
    public async Task Invitation_EmailMismatch_Returns400WithMaskedEmail()
    {
        string token = "inv-" + Guid.NewGuid().ToString("N")[..10];
        await using (var db = _app.CreateDb())
        {
            var team = new Team { Name = "Tô" + Guid.NewGuid().ToString("N")[..6] };
            db.Teams.Add(team);
            await db.SaveChangesAsync();

            db.Invitations.Add(new Invitation
            {
                Email = "nguoiduocmoin@gmail.com",
                TeamId = team.Id,
                TokenHash = AuthService.HashToken(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        AddFake(_app, "tok-mismatch", "nguokhac@gmail.com");
        var response = await PostLoginAsync(Client, "tok-mismatch", token);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("code").GetString().ShouldBe("invite.email_mismatch");
        json.GetProperty("title").GetString()!.ShouldContain("n***@gmail.com");
    }

    [Fact]
    public async Task InvitationInfo_Public_ReturnsMaskedEmailAndStatus()
    {
        string token = "inv-" + Guid.NewGuid().ToString("N")[..10];
        string teamName;
        await using (var db = _app.CreateDb())
        {
            var team = new Team { Name = "Tô" + Guid.NewGuid().ToString("N")[..6] };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            teamName = team.Name;

            db.Invitations.Add(new Invitation
            {
                Email = "congkhai@gmail.com",
                TeamId = team.Id,
                TokenHash = AuthService.HashToken(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/invitations/{token}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("emailMasked").GetString().ShouldBe("c***@gmail.com");
        json.GetProperty("teamName").GetString().ShouldBe(teamName);
        json.GetProperty("status").GetString().ShouldBe("Chờ");

        (await Client.GetAsync("/api/invitations/token-khong-ton-tai")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
