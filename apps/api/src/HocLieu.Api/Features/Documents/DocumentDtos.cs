namespace HocLieu.Features.Documents;

// ===== Form tạo/sửa (FE gửi; null = giữ mặc định/mặc định hệ thống) =====
public sealed record CreateDocumentRequest(
    string? Title,
    long? SectionId,
    short? GradeId,
    long? SubjectId,
    long? SchoolYearId,
    short? WeekNo,
    string? Scope,
    long? TeamId,
    string? Summary,
    string? DescriptionHtml,
    IReadOnlyList<long>? FileIds,
    long? CoverFileId,
    bool? AllowGuestDownload,
    string? PublishMode,
    DateTimeOffset? PublishFrom,
    DateTimeOffset? PublishUntil);

/// <summary>PUT thay toàn bộ; `UpdatedAt` = giá trị client đã đọc (concurrency, spec §8.4).</summary>
public sealed record UpdateDocumentRequest(
    string? Title,
    long? SectionId,
    short? GradeId,
    long? SubjectId,
    long? SchoolYearId,
    short? WeekNo,
    string? Scope,
    long? TeamId,
    string? Summary,
    string? DescriptionHtml,
    IReadOnlyList<long>? FileIds,
    long? CoverFileId,
    bool? AllowGuestDownload,
    string? PublishMode,
    DateTimeOffset? PublishFrom,
    DateTimeOffset? PublishUntil,
    string? UpdatedAt);

public sealed record PublishRequest(string? Mode, DateTimeOffset? From, DateTimeOffset? Until);

public sealed record BulkPublishItem(string? Kind, long Id);
public sealed record BulkPublishRequest(IReadOnlyList<BulkPublishItem>? Items, string? Mode, DateTimeOffset? From, DateTimeOffset? Until);
public sealed record BulkPublishFailure(string Kind, long Id, string Code);
public sealed record BulkPublishResult(int Applied, IReadOnlyList<BulkPublishFailure> Failed);

public sealed record TagRef(long Id, string Name);

/// <summary>1 hàng trong bảng "Tài liệu của tôi" (/gv/tai-lieu).</summary>
public sealed record MyDocumentRow(
    long Id, string Title, string Slug,
    string? SectionSlug, string? SectionName,
    short? Grade, string? SubjectName,
    string? SchoolYearName, short? WeekNo,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil, string PublishState,
    string ModerationStatus,
    int FileCount, int ViewCount, int DownloadCount,
    DateTimeOffset CreatedAt, string UpdatedAt, bool IsDeleted);

/// <summary>Chi tiết tài liệu cho GV chủ sở hữu/Admin (form sửa + tabs).</summary>
public sealed record DocumentDetailDto(
    long Id, string Title, string Slug, string? Summary, string? DescriptionHtml,
    long? SectionId, string? SectionSlug, string? SectionName,
    short? GradeId, string? GradeName,
    long? SubjectId, string? SubjectName,
    long? SchoolYearId, string? SchoolYearName,
    short? WeekNo,
    long? TeamId, string? TeamName,
    long? OwnerId, string? OwnerName,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil, string PublishState,
    string ModerationStatus, string? ModerationNote,
    bool AllowGuestDownload,
    long? CoverFileId,
    IReadOnlyList<Files.FileDto> Files,
    IReadOnlyList<TagRef> Tags,
    bool IsDeleted,
    DateTimeOffset CreatedAt, string UpdatedAt,
    int ViewCount, int DownloadCount);

// ===== Khu công khai =====

/// <summary>1 thẻ trong lưới /public/items (tài liệu + bài tập gộp).</summary>
public sealed record PublicItemRow(
    string Kind, long Id, string Slug, string Title, string? Summary,
    string? SectionSlug, string? SectionName,
    short? Grade, string? SubjectName, short? WeekNo, string? SchoolYearName,
    int QuestionCount, int? TimeLimitMinutes,
    string PublishState, DateTimeOffset? PublishUntil,
    int ViewCount, DateTimeOffset CreatedAt);

public sealed record PublicFileDto(long Id, string Name, long Size, string Ext, int? Pages, bool Ready);

public sealed record AuthorDto(string FullName, string? TeamName);

public sealed record PublicDocumentDetailDto(
    long Id, string Slug, string Title, string? Summary, string? DescriptionHtml,
    string? SectionSlug, string? SectionName,
    short? Grade, string? SubjectName, short? WeekNo, string? SchoolYearName,
    AuthorDto? Author,
    IReadOnlyList<PublicFileDto> Files,
    bool AllowGuestDownload,
    int ViewCount, string CreatedAt);

public sealed record ReportRequest(string? ItemType, long? ItemId, string? Reason, string? Detail);

public sealed record FavoriteRequest(string? ItemType, long? ItemId);
