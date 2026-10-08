namespace HocLieu.Domain.Entities;

public class Favorite
{
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public FavoriteItemType ItemType { get; set; }
    public long ItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Announcement
{
    public long Id { get; set; }
    public string Title { get; set; } = default!;
    public string BodyHtml { get; set; } = default!;
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Public;
    public long? TeamId { get; set; }
    public Team? Team { get; set; }
    public bool IsPinned { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
    public DateTimeOffset? ExpireAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class StaticPage
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string BodyMarkdown { get; set; } = "";
    public long? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class Notification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Link { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ContentReport
{
    public long Id { get; set; }
    public FavoriteItemType ItemType { get; set; }
    public long ItemId { get; set; }
    public string Reason { get; set; } = default!;
    public string? Detail { get; set; }
    public long? ReporterUserId { get; set; }
    public User? Reporter { get; set; }
    public string? ReporterIpHash { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public long? ResolvedBy { get; set; }
    public User? ResolvedByUser { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }
    public User? Actor { get; set; }
    public string Action { get; set; } = default!;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Data { get; set; } // jsonb
    public string? Ip { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = default!;
    public string Value { get; set; } = "{}"; // jsonb
    public long? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
