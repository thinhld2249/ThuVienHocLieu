using System.Security.Cryptography;
using System.Text;
using HocLieu.Common;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Teams;

/// <summary>Lỗi luồng quản trị tổ với HTTP status + code máy đọc → endpoint chuyển ProblemDetails.</summary>
public sealed class TeamFlowException(int status, string code, string title) : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
}

/// <summary>
/// §2.2, §3.3, §5.3: quản trị tổ — hàng chờ duyệt, lời mời, thành viên & vai trò,
/// thông báo tổ, thống kê, quản trị Admin. Phân quyền theo ViewerContext (mẫu của repo).
/// </summary>
public sealed class TeamsService(
    AppDbContext db,
    IAuditLogger audit,
    IEmailSender email,
    TimeProvider time)
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);
    private const int MaxInvitesPerCall = 100;

    // ===== Phân quyền (spec §2.2) =====

    /// <summary>Viewer là thành viên tổ hoặc Admin; không → 404 (không lộ sự tồn tại tổ).</summary>
    private async Task<Team> EnsureTeamMemberAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null)
            throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        var isMember = viewer.UserId is not null && viewer.TeamIds.Contains(teamId);
        if (!viewer.IsAdmin && !isMember)
            throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        return team;
    }

    /// <summary>Lead | Deputy của tổ | Admin (duyệt GV xin vào, mời, thông báo tổ).</summary>
    private static void EnsureCanManage(ViewerContext viewer, long teamId)
    {
        if (viewer.IsAdmin || viewer.LeadOrDeputyTeamIds.Contains(teamId))
            return;
        throw new TeamFlowException(403, ErrorCodes.TeamNotManage,
            "Chỉ tổ trưởng, tổ phó hoặc quản trị viên mới có thể thao tác này.");
    }

    /// <summary>Lead của tổ | Admin (đặt/bỏ tổ phó, gỡ thành viên — spec §2.2).</summary>
    private static void EnsureCanOwn(ViewerContext viewer, long teamId)
    {
        if (viewer.IsAdmin || viewer.LeadTeamIds.Contains(teamId))
            return;
        throw new TeamFlowException(403, ErrorCodes.TeamNotLead,
            "Chỉ tổ trưởng hoặc quản trị viên mới có thể thao tác này.");
    }

    // ===== Chi tiết tổ & thành viên (thành viên tổ xem được) =====

    public async Task<TeamDetailDto> GetTeamDetailAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        var (memberCount, leadName) = await TeamCountsAsync(teamId, ct);
        string? viewerRole = null;
        if (viewer.UserId is long uid)
        {
            var row = await db.TeamMembers.AsNoTracking()
                .Where(tm => tm.TeamId == teamId && tm.UserId == uid)
                .Select(tm => tm.Role.ToString())
                .FirstOrDefaultAsync(ct);
            viewerRole = row;
        }
        return new TeamDetailDto(
            team.Id, team.Name, team.Description, GradeName(team),
            memberCount, leadName, viewerRole, viewer.IsAdmin);
    }

    public async Task<List<TeamMemberDto>> GetMembersAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        await EnsureTeamMemberAsync(viewer, teamId, ct);
        var members = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.TeamId == teamId)
            .OrderBy(tm => tm.Role == TeamRole.Lead ? 0 : tm.Role == TeamRole.Deputy ? 1 : 2)
            .ThenBy(tm => tm.User.FullName)
            .Select(tm => new { tm.UserId, tm.Role, tm.JoinedAt, tm.User.FullName, tm.User.Email, tm.User.AvatarUrl })
            .ToListAsync(ct);

        var userIds = members.Select(m => m.UserId).ToList();
        var counts = await ContentCountsByOwnerAsync(userIds, ct);
        return members
            .Select(m => new TeamMemberDto(
                m.UserId, m.FullName, m.Email, m.AvatarUrl,
                m.Role.ToString(), m.JoinedAt, counts.GetValueOrDefault(m.UserId)))
            .ToList();
    }

    private async Task<(int MemberCount, string? LeadName)> TeamCountsAsync(long teamId, CancellationToken ct)
    {
        var rows = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.TeamId == teamId)
            .Select(tm => new { tm.Role, tm.User.FullName })
            .ToListAsync(ct);
        return (rows.Count, rows.FirstOrDefault(r => r.Role == TeamRole.Lead)?.FullName);
    }

    // ===== Hàng chờ duyệt (Lead | Deputy | Admin) =====

    public async Task<List<JoinRequestDto>> GetJoinRequestsAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Pending && u.RequestedTeamId == teamId)
            .OrderBy(u => u.UpdatedAt)
            .ToListAsync(ct);
        return users
            .Select(u => new JoinRequestDto(u.Id, u.FullName, u.Email, u.AvatarUrl, u.Phone, u.UpdatedAt))
            .ToList();
    }

    public async Task<List<JoinActionResult>> ApproveJoinRequestsAsync(
        ViewerContext viewer, long teamId, IEnumerable<long> userIds, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        EnsureCanManage(viewer, teamId);
        if (!team.IsActive)
            throw new TeamFlowException(400, "team.inactive", "Tổ đã ngừng hoạt động.");

        var results = new List<JoinActionResult>();
        foreach (var userId in userIds.Distinct())
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null || user.Status != UserStatus.Pending || user.RequestedTeamId != teamId)
            {
                results.Add(new JoinActionResult(userId, user?.FullName ?? "—", false,
                    "Yêu cầu không còn hợp lệ (tài khoản đã thay đổi trạng thái)."));
                continue;
            }
            await ApproveSingleAsync(user, team, viewer, ct);
            results.Add(new JoinActionResult(userId, user.FullName, true, null));
        }
        return results;
    }

    public async Task<JoinActionResult> RejectJoinRequestAsync(
        ViewerContext viewer, long teamId, long userId, string? reason, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Status != UserStatus.Pending || user.RequestedTeamId != teamId)
            throw new TeamFlowException(404, "not_found", "Không tìm thấy yêu cầu vào tổ.");

        var now = time.GetUtcNow();
        user.Status = UserStatus.Rejected;
        user.StatusReason = string.IsNullOrWhiteSpace(reason)
            ? $"Yêu cầu tham gia tổ {team.Name} bị từ chối."
            : reason.Trim();
        user.RequestedTeamId = null;
        user.UpdatedAt = now;
        await NotifyAsync(user.Id, "team.rejected",
            $"Yêu cầu vào tổ {team.Name} đã bị từ chối", "/cho-duyet");
        await email.SendAsync(user.Email, $"Yêu cầu vào tổ {team.Name}",
            BuildMailBody($"Yêu cầu tham gia tổ {team.Name} của bạn đã bị từ chối.",
                string.IsNullOrWhiteSpace(reason) ? null : $"Lý do: {reason.Trim()}"), ct);
        await audit.LogAsync("team.join_request.rejected", "user", userId.ToString(),
            new { teamId, userId, reason }, ct);
        await db.SaveChangesAsync(ct);
        return new JoinActionResult(userId, user.FullName, true, null);
    }

    private async Task ApproveSingleAsync(User user, Team team, ViewerContext viewer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        user.Status = UserStatus.Active;
        user.StatusReason = null;
        user.ApprovedBy = viewer.UserId;
        user.ApprovedAt = now;
        user.RequestedTeamId = null;
        user.UpdatedAt = now;
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = user.Id,
            Role = TeamRole.Member,
            JoinedAt = now,
        });
        await NotifyAsync(user.Id, "team.approved",
            $"Bạn đã được duyệt vào tổ {team.Name}", "/gv");
        await email.SendAsync(user.Email, $"Đã được duyệt vào tổ {team.Name}",
            BuildMailBody($"Yêu cầu tham gia tổ {team.Name} của bạn đã được duyệt.", null), ct);
        await audit.LogAsync("team.join_request.approved", "user", user.Id.ToString(),
            new { teamId = team.Id, userId = user.Id }, ct);
    }

    // ===== Thành viên & vai trò (đặt/bỏ tổ phó, gỡ — Lead | Admin) =====

    public async Task<TeamMemberDto> SetMemberRoleAsync(
        ViewerContext viewer, long teamId, long userId, string role, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanOwn(viewer, teamId);
        var newRole = role switch
        {
            "Member" => TeamRole.Member,
            "Deputy" => TeamRole.Deputy,
            _ => throw new TeamFlowException(422, "validation", "Vai trò phải là Member hoặc Deputy."),
        };
        var member = await db.TeamMembers
            .Include(tm => tm.User)
            .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Thành viên không thuộc tổ này.");
        if (member.Role == TeamRole.Lead)
            throw new TeamFlowException(400, ErrorCodes.TeamNotLead,
                "Tổ trưởng được thay đổi qua chức năng bổ nhiệm của quản trị viên.");

        member.Role = newRole;
        await NotifyAsync(userId, "team.role_changed",
            newRole == TeamRole.Deputy
                ? $"Bạn được bổ nhiệm làm tổ phó tổ {team.Name}"
                : $"Vai trò của bạn trong tổ {team.Name} đã được đổi thành thành viên",
            $"/gv/to/{team.Id}");
        await audit.LogAsync("team.member.role_changed", "team_member", $"{teamId}:{userId}",
            new { teamId, userId, role = newRole.ToString() }, ct);
        await db.SaveChangesAsync(ct);
        var counts = await ContentCountsByOwnerAsync([userId], ct);
        return new TeamMemberDto(userId, member.User.FullName, member.User.Email, member.User.AvatarUrl,
            newRole.ToString(), member.JoinedAt, counts.GetValueOrDefault(userId, 0));
    }

    public async Task RemoveMemberAsync(ViewerContext viewer, long teamId, long userId, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanOwn(viewer, teamId);
        var member = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Thành viên không thuộc tổ này.");
        if (member.Role == TeamRole.Lead)
            throw new TeamFlowException(400, ErrorCodes.TeamNotLead,
                "Hãy bổ nhiệm tổ trưởng khác trước khi gỡ tổ trưởng khỏi tổ.");

        db.TeamMembers.Remove(member);
        // Gỡ khỏi tổ ≠ khóa tài khoản; nội dung giữ team_id cũ (spec §3.2)
        await NotifyAsync(userId, "team.removed",
            $"Bạn đã được gỡ khỏi tổ {team.Name}", "/gv");
        await audit.LogAsync("team.member.removed", "team_member", $"{teamId}:{userId}",
            new { teamId, userId }, ct);
        await db.SaveChangesAsync(ct);
    }

    // ===== Lời mời (Lead | Deputy | Admin) =====

    public async Task<List<InvitationDto>> GetInvitationsAsync(ViewerContext viewer, long teamId, string? baseUrl, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var invites = await db.Invitations.AsNoTracking()
            .Where(i => i.TeamId == teamId)
            .Include(i => i.Inviter)
            .Include(i => i.AcceptedUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return invites
            .Select(i => ToInvitationDto(i, baseUrl, now, team.Name))
            .ToList();
    }

    public async Task<List<InvitationResult>> CreateInvitationsAsync(
        ViewerContext viewer, long teamId, IEnumerable<string> rawEmails,
        TeamRole role, string? message, string? baseUrl, bool allowLead, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        if (!team.IsActive)
            throw new TeamFlowException(400, "team.inactive", "Tổ đã ngừng hoạt động — không thể mời thành viên.");
        if (role == TeamRole.Lead && !allowLead)
            throw new TeamFlowException(422, "validation",
                "Chỉ quản trị viên mới có thể mời làm tổ trưởng.");

        var emails = ParseEmails(rawEmails);
        if (emails.Count == 0)
            throw new TeamFlowException(422, "validation", "Chưa có email nào hợp lệ để mời.");
        if (emails.Count > MaxInvitesPerCall)
            throw new TeamFlowException(422, "validation", $"Mời tối đa {MaxInvitesPerCall} email mỗi lần.");

        var now = time.GetUtcNow();
        var results = new List<InvitationResult>();
        var openInvites = await db.Invitations
            .Where(i => i.TeamId == teamId && i.AcceptedAt == null && i.RevokedAt == null
                && emails.Contains(i.Email))
            .ToListAsync(ct);

        foreach (var mail in emails)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == mail, ct);
            if (existing is not null)
            {
                results.Add(existing.Status switch
                {
                    UserStatus.Active => await HandleExistingUserAsync(existing, team, role, baseUrl, ct),
                    UserStatus.Pending => await ApprovePendingUserAsync(existing, team, role, viewer, ct),
                    _ => new InvitationResult(mail, "skipped",
                        existing.Status == UserStatus.Suspended
                            ? "Tài khoản đang bị khóa — không thể mời."
                            : "Tài khoản đã bị từ chối trước đó — liên hệ quản trị viên.", null, null),
                });
                continue;
            }

            var open = openInvites.FirstOrDefault(i =>
                string.Equals(i.Email, mail, StringComparison.OrdinalIgnoreCase));
            if (open is not null)
            {
                results.Add(new InvitationResult(mail, "existing_invite",
                    "Đã có lời mời đang chờ cho email này.", open.Id,
                    LinkFor(open, baseUrl)));
                continue;
            }

            var (token, invite) = NewInvitation(mail, team, role, message, viewer, now);
            db.Invitations.Add(invite);
            var link = $"{NormalizeBase(baseUrl)}/moi/{token}";
            await email.SendAsync(mail, $"Lời mời tham gia tổ {team.Name} trên Học Liệu",
                BuildMailBody(
                    $"Bạn được mời tham gia tổ <strong>{team.Name}</strong> trên cổng Học Liệu.",
                    $"Mở link dưới đây để đăng nhập và tham gia (hết hạn sau 7 ngày):<br/><a href=\"{link}\">{link}</a>",
                    message), ct);
            await audit.LogAsync("team.invitation.created", "invitation", invite.Id.ToString(),
                new { teamId, email = mail }, ct);
            results.Add(new InvitationResult(mail, "invited", null, invite.Id, link));
        }

        await db.SaveChangesAsync(ct);
        return results;
    }

    public async Task<InvitationResult> ResendInvitationAsync(
        ViewerContext viewer, long teamId, Guid invId, string? baseUrl, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var invite = await db.Invitations.Include(i => i.Team)
            .FirstOrDefaultAsync(i => i.Id == invId && i.TeamId == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Lời mời không tồn tại.");
        if (invite.AcceptedAt is not null)
            throw new TeamFlowException(400, "invite.accepted", "Lời mời đã được nhận.");
        if (invite.RevokedAt is not null)
            throw new TeamFlowException(400, "invite.revoked", "Lời mời đã bị thu hồi.");

        var now = time.GetUtcNow();
        var token = NewToken();
        invite.TokenText = token;
        invite.TokenHash = HashToken(token);
        invite.ExpiresAt = now + InviteLifetime;
        var link = $"{NormalizeBase(baseUrl)}/moi/{token}";
        await email.SendAsync(invite.Email, $"Lời mời tham gia tổ {team.Name} trên Học Liệu",
            BuildMailBody($"Lời mời tham gia tổ <strong>{team.Name}</strong> (gửi lại).",
                $"<a href=\"{link}\">{link}</a>"), ct);
        await audit.LogAsync("team.invitation.resent", "invitation", invId.ToString(),
            new { teamId, invite.Email }, ct);
        await db.SaveChangesAsync(ct);
        return new InvitationResult(invite.Email, "invited", null, invite.Id, link);
    }

    public async Task RevokeInvitationAsync(ViewerContext viewer, long teamId, Guid invId, CancellationToken ct)
    {
        await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invId && i.TeamId == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Lời mời không tồn tại.");
        if (invite.AcceptedAt is not null)
            throw new TeamFlowException(400, "invite.accepted", "Lời mời đã được nhận — không thể thu hồi.");
        if (invite.RevokedAt is not null)
            throw new TeamFlowException(400, "invite.revoked", "Lời mời đã bị thu hồi.");
        invite.RevokedAt = time.GetUtcNow();
        await audit.LogAsync("team.invitation.revoked", "invitation", invId.ToString(),
            new { teamId, invite.Email }, ct);
        await db.SaveChangesAsync(ct);
    }

    // ===== Thông báo tổ (thành viên xem; Lead | Deputy | Admin đăng) =====

    public async Task<List<TeamAnnouncementDto>> GetTeamAnnouncementsAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        await EnsureTeamMemberAsync(viewer, teamId, ct);
        var now = time.GetUtcNow();
        var rows = await db.Announcements.AsNoTracking()
            .Where(a => a.Audience == AnnouncementAudience.Team && a.TeamId == teamId
                && (a.PublishAt == null || a.PublishAt <= now)
                && (a.ExpireAt == null || now < a.ExpireAt))
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        var authorIds = rows.Select(r => r.CreatedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows
            .Select(a => new TeamAnnouncementDto(a.Id, a.Title, a.BodyHtml, a.IsPinned,
                names.GetValueOrDefault(a.CreatedBy, "—"), a.CreatedAt))
            .ToList();
    }

    public async Task<TeamAnnouncementDto> PostTeamAnnouncementAsync(
        ViewerContext viewer, long teamId, string title, string bodyHtml, bool isPinned,
        DateTimeOffset? expireAt, CancellationToken ct)
    {
        var team = await EnsureTeamMemberAsync(viewer, teamId, ct);
        EnsureCanManage(viewer, teamId);
        var now = time.GetUtcNow();
        title = (title ?? "").Trim();
        if (title.Length < 3 || title.Length > 200)
            throw new TeamFlowException(422, "validation", "Tiêu đề phải từ 3 đến 200 ký tự.");
        var announcement = new Announcement
        {
            Title = title,
            BodyHtml = HtmlSanitize.Clean(bodyHtml),
            Audience = AnnouncementAudience.Team,
            TeamId = teamId,
            IsPinned = isPinned,
            ExpireAt = expireAt,
            CreatedBy = viewer.UserId ?? 0,
            CreatedAt = now,
        };
        db.Announcements.Add(announcement);
        // Thông báo in-app cho mọi thành viên tổ
        var memberIds = await db.TeamMembers
            .Where(tm => tm.TeamId == teamId)
            .Select(tm => tm.UserId)
            .ToListAsync(ct);
        foreach (var uid in memberIds)
            db.Notifications.Add(new Notification
            {
                UserId = uid,
                Type = "team.announcement",
                Title = $"Thông báo tổ {team.Name}: {title}",
                Link = $"/gv/to/{teamId}",
                CreatedAt = now,
            });
        await audit.LogAsync("team.announcement.posted", "announcement", announcement.Id.ToString(),
            new { teamId, title }, ct);
        await db.SaveChangesAsync(ct);
        return new TeamAnnouncementDto(announcement.Id, announcement.Title, announcement.BodyHtml,
            announcement.IsPinned, "—", announcement.CreatedAt);
    }

    // ===== Thống kê tổ (thành viên xem) =====

    public async Task<TeamStatsDto> GetTeamStatsAsync(ViewerContext viewer, long teamId, CancellationToken ct)
    {
        await EnsureTeamMemberAsync(viewer, teamId, ct);
        var members = await db.TeamMembers.AsNoTracking()
            .Where(tm => tm.TeamId == teamId)
            .Select(tm => new { tm.UserId, tm.Role, tm.User.FullName })
            .ToListAsync(ct);
        var userIds = members.Select(m => m.UserId).ToList();
        var counts = await ContentCountsByOwnerAsync(userIds, ct);
        var docTotal = await db.Documents.CountAsync(d => !d.IsDeleted && d.TeamId == teamId, ct);
        var quizTotal = await db.Quizzes.CountAsync(q => !q.IsDeleted && q.TeamId == teamId, ct);
        return new TeamStatsDto(
            members.Count, docTotal, quizTotal,
            members
                .OrderBy(m => m.Role == TeamRole.Lead ? 0 : m.Role == TeamRole.Deputy ? 1 : 2)
                .ThenBy(m => m.FullName)
                .Select(m => new TeamMemberStatDto(m.UserId, m.FullName, m.Role.ToString(),
                    counts.GetValueOrDefault(m.UserId), counts.GetValueOrDefault(m.UserId)))
                .ToList());
    }

    // ===== Quản trị tổ (Admin) =====

    public async Task<List<TeamDto>> AdminListTeamsAsync(CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var teamIds = teams.Select(t => t.Id).ToList();
        var rows = await db.TeamMembers.AsNoTracking()
            .Where(tm => teamIds.Contains(tm.TeamId))
            .Select(tm => new { tm.TeamId, tm.Role, tm.User.FullName })
            .ToListAsync(ct);
        return teams
            .Select(t =>
            {
                var mine = rows.Where(r => r.TeamId == t.Id).ToList();
                return new TeamDto(t.Id, t.Name, t.Description, GradeName(t), mine.Count(),
                    mine.FirstOrDefault(r => r.Role == TeamRole.Lead)?.FullName, t.IsActive);
            })
            .ToList();
    }

    public async Task<TeamDto> AdminCreateTeamAsync(string name, string? description, short? gradeId, CancellationToken ct)
    {
        ValidateTeamName(name);
        if (gradeId is not null && await db.Grades.AllAsync(g => g.Id != gradeId.Value, ct))
            throw new TeamFlowException(422, "validation", "Khối không tồn tại.");
        if (await db.Teams.AnyAsync(t => t.Name == name.Trim(), ct))
            throw new TeamFlowException(409, "conflict", "Đã có tổ cùng tên.");

        var now = time.GetUtcNow();
        var team = new Team
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            GradeId = gradeId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Teams.Add(team);
        await audit.LogAsync("admin.team.created", "team", team.Id.ToString(),
            new { team.Name }, ct);
        await db.SaveChangesAsync(ct);
        return new TeamDto(team.Id, team.Name, team.Description, GradeName(team), 0, null, true);
    }

    public async Task<TeamDto> AdminUpdateTeamAsync(
        long teamId, string? name, string? description, short? gradeId, bool? isActive, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        if (name is not null)
        {
            ValidateTeamName(name);
            if (await db.Teams.AnyAsync(t => t.Name == name.Trim() && t.Id != teamId, ct))
                throw new TeamFlowException(409, "conflict", "Đã có tổ cùng tên.");
            team.Name = name.Trim();
        }
        if (description is not null)
            team.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (gradeId.HasValue)
        {
            if (await db.Grades.AllAsync(g => g.Id != gradeId.Value, ct))
                throw new TeamFlowException(422, "validation", "Khối không tồn tại.");
            team.GradeId = gradeId;
        }
        team.IsActive = isActive ?? team.IsActive;
        team.UpdatedAt = time.GetUtcNow();
        await audit.LogAsync("admin.team.updated", "team", teamId.ToString(),
            new { name = team.Name, isActive = team.IsActive }, ct);
        await db.SaveChangesAsync(ct);
        var (count, lead) = await TeamCountsAsync(teamId, ct);
        return new TeamDto(team.Id, team.Name, team.Description, GradeName(team), count, lead, team.IsActive);
    }

    /// <summary>Spec §5.4: "ngưng hoạt động tổ" — không xóa cứng (thành viên & nội dung còn trỏ).</summary>
    public async Task AdminDeactivateTeamAsync(long teamId, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        team.IsActive = false;
        team.UpdatedAt = time.GetUtcNow();
        await audit.LogAsync("admin.team.deactivated", "team", teamId.ToString(),
            new { team.Name }, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Spec §5.4: bổ nhiệm Tổ trưởng — người cũ thành Member; chỉ chọn GV Active.</summary>
    public async Task<TeamDto> AdminSetLeadAsync(long teamId, long userId, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new TeamFlowException(404, "not_found", "Tổ không tồn tại.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            throw new TeamFlowException(404, "not_found", "Giáo viên không tồn tại.");
        if (user.Status != UserStatus.Active)
            throw new TeamFlowException(422, "validation",
                "Chỉ giáo viên đang hoạt động (Active) mới có thể làm tổ trưởng.");

        var now = time.GetUtcNow();
        var member = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId, ct);
        if (member is not null && member.Role == TeamRole.Lead)
            throw new TeamFlowException(400, "conflict", "Giáo viên này đã là tổ trưởng của tổ.");

        // Người cũ → Member (unique index 1 Lead/tổ được giữ nguyên)
        var oldLead = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.Role == TeamRole.Lead, ct);
        if (oldLead is not null && oldLead.UserId != userId)
        {
            oldLead.Role = TeamRole.Member;
            await NotifyAsync(oldLead.UserId, "team.lead_changed",
                $"Bạn không còn là tổ trưởng tổ {team.Name}", $"/gv/to/{teamId}");
        }
        if (member is null)
            db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = userId, Role = TeamRole.Lead, JoinedAt = now });
        else
            member.Role = TeamRole.Lead;

        await NotifyAsync(userId, "team.lead_appointed",
            $"Bạn được bổ nhiệm làm tổ trưởng tổ {team.Name}", $"/gv/to/{teamId}");
        await audit.LogAsync("team.lead_appointed", "team", teamId.ToString(),
            new { teamId, userId = user.Id, oldLeadId = oldLead?.UserId }, ct);
        await db.SaveChangesAsync(ct);

        var (count, lead) = await TeamCountsAsync(teamId, ct);
        return new TeamDto(team.Id, team.Name, team.Description, GradeName(team), count, lead, team.IsActive);
    }

    // ===== Lời mời toàn hệ thống (Admin) =====

    public async Task<List<InvitationDto>> AdminListInvitationsAsync(long? teamId, string? status, string? baseUrl, CancellationToken ct)
    {
        IQueryable<Invitation> q = db.Invitations.AsNoTracking();
        if (teamId is not null)
            q = q.Where(i => i.TeamId == teamId);
        q = q.Include(i => i.Team).Include(i => i.Inviter).Include(i => i.AcceptedUser);
        var invites = await q.OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(ct);
        var now = time.GetUtcNow();
        return invites
            .Select(i => ToInvitationDto(i, baseUrl, now, i.Team?.Name))
            .Where(x => status is null || x.Status == status)
            .ToList();
    }

    // ===== Helpers =====

    private async Task<InvitationResult> HandleExistingUserAsync(
        User user, Team team, TeamRole role, string? baseUrl, CancellationToken ct)
    {
        var member = await db.TeamMembers.AnyAsync(tm => tm.TeamId == team.Id && tm.UserId == user.Id, ct);
        if (member)
            return new InvitationResult(user.Email, "existing", "Đã là thành viên của tổ.", null, null);

        var now = time.GetUtcNow();
        await EnsureLeadSlotAsync(team.Id, role, ct);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = role, JoinedAt = now });
        await NotifyAsync(user.Id, "team.invited_added",
            $"Bạn đã được thêm vào tổ {team.Name}", $"/gv/to/{team.Id}");
        await email.SendAsync(user.Email, $"Đã được thêm vào tổ {team.Name}",
            BuildMailBody($"Bạn đã được thêm vào tổ <strong>{team.Name}</strong>.", null), ct);
        await audit.LogAsync("team.member.invited_added", "team_member", $"{team.Id}:{user.Id}",
            new { teamId = team.Id, userId = user.Id, role = role.ToString() }, ct);
        return new InvitationResult(user.Email, "added", null, null, null);
    }

    private async Task<InvitationResult> ApprovePendingUserAsync(
        User user, Team team, TeamRole role, ViewerContext viewer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        user.Status = UserStatus.Active;
        user.StatusReason = null;
        user.ApprovedBy = viewer.UserId;
        user.ApprovedAt = now;
        user.RequestedTeamId = null;
        user.UpdatedAt = now;
        await EnsureLeadSlotAsync(team.Id, role, ct);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = user.Id, Role = role, JoinedAt = now });
        await NotifyAsync(user.Id, "team.approved",
            $"Bạn đã được duyệt vào tổ {team.Name}", $"/gv/to/{team.Id}");
        await email.SendAsync(user.Email, $"Đã được duyệt vào tổ {team.Name}",
            BuildMailBody($"Tài khoản của bạn đã được duyệt và tham gia tổ <strong>{team.Name}</strong>.", null), ct);
        await audit.LogAsync("team.member.invited_approved", "team_member", $"{team.Id}:{user.Id}",
            new { teamId = team.Id, userId = user.Id, role = role.ToString() }, ct);
        return new InvitationResult(user.Email, "approved", null, null, null);
    }

    /// <summary>Vai trò Lead: hạ Lead cũ (nếu có) trước khi thêm Lead mới — giữ unique index 1 Lead/tổ.</summary>
    private async Task EnsureLeadSlotAsync(long teamId, TeamRole role, CancellationToken ct)
    {
        if (role != TeamRole.Lead)
            return;
        var oldLead = await db.TeamMembers.FirstOrDefaultAsync(
            tm => tm.TeamId == teamId && tm.Role == TeamRole.Lead, ct);
        if (oldLead is not null)
            oldLead.Role = TeamRole.Member;
    }

    private static (string Token, Invitation Invite) NewInvitation(
        string email, Team team, TeamRole role, string? message, ViewerContext viewer, DateTimeOffset now)
    {
        var token = NewToken();
        return (token, new Invitation
        {
            Email = email,
            TeamId = team.Id,
            TeamRole = role,
            TokenText = token,
            TokenHash = HashToken(token),
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            InvitedBy = viewer.UserId,
            ExpiresAt = now + InviteLifetime,
            CreatedAt = now,
        });
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static byte[] HashToken(string token)
        => SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));

    public static string InvitationStatus(Invitation i, DateTimeOffset now)
        => i.RevokedAt is not null ? "Đã thu hồi"
        : i.AcceptedAt is not null ? "Đã nhận"
        : i.ExpiresAt < now ? "Hết hạn"
        : "Chờ";

    private static string? LinkFor(Invitation i, string? baseUrl)
        => i.TokenText is null ? null : $"{NormalizeBase(baseUrl)}/moi/{i.TokenText}";

    private static InvitationDto ToInvitationDto(Invitation i, string? baseUrl, DateTimeOffset now, string? teamName)
        => new(
            i.Id, i.Email, InvitationStatus(i, now), LinkFor(i, baseUrl),
            i.ExpiresAt, i.CreatedAt, i.TeamRole.ToString(),
            i.Inviter?.FullName, i.AcceptedUser?.FullName, teamName);

    /// <summary>Tách nhiều email (xuống dòng, phẩy, chấm phẩy) — giữ thứ tự, bỏ trùng, bỏ ô rác.</summary>
    public static List<string> ParseEmails(IEnumerable<string> raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in raw ?? [])
        {
            foreach (var part in chunk.Split(['\n', '\r', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var email = part.Trim();
                if (email.Contains('@') && email.Contains('.') && seen.Add(email))
                    result.Add(email);
            }
        }
        return result;
    }

    private static void ValidateTeamName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length < 2 || n.Length > 100)
            throw new TeamFlowException(422, "validation", "Tên tổ phải từ 2 đến 100 ký tự.");
    }

    private static string? GradeName(Team team)
        => team.GradeId is short gid ? $"Khối {gid}" : null;

    private async Task<Dictionary<long, int>> ContentCountsByOwnerAsync(List<long> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<long, int>();
        var docs = await db.Documents.Where(d => !d.IsDeleted && d.OwnerId != null && userIds.Contains(d.OwnerId.Value))
            .GroupBy(d => d.OwnerId!.Value)
            .Select(g => new { Owner = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Owner, x => x.C, ct);
        var quizzes = await db.Quizzes.Where(q => !q.IsDeleted && q.OwnerId != null && userIds.Contains(q.OwnerId.Value))
            .GroupBy(q => q.OwnerId!.Value)
            .Select(g => new { Owner = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Owner, x => x.C, ct);
        foreach (var (uid, c) in quizzes)
            docs[uid] = docs.GetValueOrDefault(uid) + c;
        return docs;
    }

    private Task NotifyAsync(long userId, string type, string title, string? link)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Link = link,
            CreatedAt = time.GetUtcNow(),
        });
        return Task.CompletedTask;
    }

    private static string BuildMailBody(string intro, string? middle, string? message = null)
    {
        var sb = new StringBuilder("<p>").Append(intro).Append("</p>");
        if (!string.IsNullOrWhiteSpace(middle))
            sb.Append("<p>").Append(middle).Append("</p>");
        if (!string.IsNullOrWhiteSpace(message))
            sb.Append("<p><em>Lời nhắn: ").Append(message).Append("</em></p>");
        sb.Append("<p>— Hệ thống Học Liệu</p>");
        return sb.ToString();
    }

    private static string NormalizeBase(string? baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.TrimEnd('/');
}
