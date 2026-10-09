using HocLieu.Domain;
using HocLieu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Common;

/// <summary>
/// §4.3–4.4: ai thấy nội dung nào.
/// is_live(now) = không xóa ∧ Approved ∧ (Visible ∨ (Scheduled ∧ trong khung giờ)).
/// VisibleTo = (is_live ∧ scope cho phép) ∨ (người tạo ∨ Lead|Deputy của tổ nội dung ∨ Admin).
/// Khách truy cập nội dung không thấy được → endpoint trả 404 (không phải 403).
/// </summary>
public record ViewerContext(
    long? UserId, bool IsAdmin,
    IReadOnlySet<long> TeamIds,
    IReadOnlySet<long> LeadOrDeputyTeamIds,
    IReadOnlySet<long> LeadTeamIds);

public static class Visibility
{
    public static bool IsLive(this Document d, DateTimeOffset now)
        => d.ModerationStatus == ModerationStatus.Approved
           && (d.PublishMode == PublishMode.Visible
               || (d.PublishMode == PublishMode.Scheduled
                   && (d.PublishFrom is null || d.PublishFrom <= now)
                   && (d.PublishUntil is null || now < d.PublishUntil)));

    public static bool IsLive(this Quiz q, DateTimeOffset now)
        => q.ModerationStatus == ModerationStatus.Approved
           && (q.PublishMode == PublishMode.Visible
               || (q.PublishMode == PublishMode.Scheduled
                   && (q.PublishFrom is null || q.PublishFrom <= now)
                   && (q.PublishUntil is null || now < q.PublishUntil)));

    /// <summary>Trạng thái hiển thị cho UI (spec §4.4): Đang ẩn · Đang hiện · Sắp mở · Đang mở · Đã đóng.</summary>
    public static PublishState GetPublishState(PublishMode mode, DateTimeOffset? from, DateTimeOffset? until, DateTimeOffset now)
    {
        if (mode == PublishMode.Visible)
            return PublishState.Visible;
        if (mode == PublishMode.Hidden)
            return PublishState.Hidden;
        if (from is not null && now < from)
            return PublishState.ScheduledUpcoming;
        if (until is not null && now >= until)
            return PublishState.Closed;
        return PublishState.ScheduledOpen;
    }

    public enum PublishState { Hidden, Visible, ScheduledUpcoming, ScheduledOpen, Closed }

    public static bool ScopeAllows(this ViewerContext viewer, ContentScope scope, long? contentTeamId)
    {
        return scope switch
        {
            ContentScope.Public => true,
            ContentScope.Teachers => viewer.UserId is not null,
            ContentScope.Team => viewer.UserId is not null && contentTeamId is not null && viewer.TeamIds.Contains(contentTeamId.Value),
            ContentScope.Private => false,
            _ => false,
        };
    }

    public static bool CanManage(this ViewerContext viewer, long? ownerId, long? teamId)
        => viewer.IsAdmin
           || (ownerId is not null && ownerId == viewer.UserId)
           || (teamId is not null && viewer.LeadOrDeputyTeamIds.Contains(teamId.Value));

    public static IQueryable<Document> VisibleTo(this IQueryable<Document> q, ViewerContext viewer, DateTimeOffset now)
    {
        var userId = viewer.UserId;
        var isAdmin = viewer.IsAdmin;
        var teamIds = viewer.TeamIds.ToList();
        var leadTeamIds = viewer.LeadOrDeputyTeamIds.ToList();

        return q.Where(d =>
            (d.ModerationStatus == ModerationStatus.Approved
             && (d.PublishMode == PublishMode.Visible
                 || (d.PublishMode == PublishMode.Scheduled
                     && (d.PublishFrom == null || d.PublishFrom <= now)
                     && (d.PublishUntil == null || now < d.PublishUntil))))
            && (d.Scope == ContentScope.Public
                || (d.Scope == ContentScope.Teachers && userId != null)
                || (d.Scope == ContentScope.Team && d.TeamId != null && teamIds.Contains(d.TeamId.Value)))
            || (isAdmin
                || d.OwnerId == userId
                || (d.TeamId != null && leadTeamIds.Contains(d.TeamId.Value))));
    }

    /// <summary>§4.3: quiz đơn lẻ có thấy được với viewer không (endpoint công khai: không thấy → 404).</summary>
    public static bool QuizVisibleTo(Quiz q, ViewerContext viewer, DateTimeOffset now)
        => !q.IsDeleted
           && (((!q.ClassOnly) && q.IsLive(now) && viewer.ScopeAllows(q.Scope, q.TeamId))
               || viewer.IsAdmin
               || (viewer.UserId is not null && q.OwnerId == viewer.UserId)
               || (q.TeamId is not null && viewer.LeadOrDeputyTeamIds.Contains(q.TeamId.Value)));

    /// <summary>
    /// Trang giới thiệu quiz công khai (§5.1): khách được xem intro (trạng thái
    /// Sắp mở/Đang mở/Đã đóng) của quiz phạm vi Public + Scheduled dù ngoài khung giờ
    /// — link hẹn giờ được GV chia sẻ cho học sinh. Hidden / scope khác vẫn 404 (M4).
    /// Quiz ClassOnly không có intro công khai (chỉ vào được qua mã giao bài).
    /// Việc BẮT ĐẦU làm bài vẫn gate chặt bằng IsLive (AttemptsService).
    /// </summary>
    public static bool QuizIntroVisibleTo(Quiz q, ViewerContext viewer, DateTimeOffset now)
        => !q.IsDeleted
           && (QuizVisibleTo(q, viewer, now)
               || (viewer.UserId is null
                   && !q.ClassOnly
                   && q.Scope == ContentScope.Public
                   && q.ModerationStatus == ModerationStatus.Approved
                   && q.PublishMode == PublishMode.Scheduled));

    public static IQueryable<Quiz> VisibleTo(this IQueryable<Quiz> q, ViewerContext viewer, DateTimeOffset now)
    {
        var userId = viewer.UserId;
        var isAdmin = viewer.IsAdmin;
        var teamIds = viewer.TeamIds.ToList();
        var leadTeamIds = viewer.LeadOrDeputyTeamIds.ToList();

        return q.Where(d =>
            (!d.ClassOnly
             && d.ModerationStatus == ModerationStatus.Approved
             && (d.PublishMode == PublishMode.Visible
                 || (d.PublishMode == PublishMode.Scheduled
                     && (d.PublishFrom == null || d.PublishFrom <= now)
                     && (d.PublishUntil == null || now < d.PublishUntil))))
            && (d.Scope == ContentScope.Public
                || (d.Scope == ContentScope.Teachers && userId != null)
                || (d.Scope == ContentScope.Team && d.TeamId != null && teamIds.Contains(d.TeamId.Value)))
            || (isAdmin
                || d.OwnerId == userId
                || (d.TeamId != null && leadTeamIds.Contains(d.TeamId.Value))));
    }
}
