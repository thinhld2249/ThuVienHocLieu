namespace HocLieu.Domain.Entities;

public class FileEntity
{
    public long Id { get; set; }
    public long? OwnerId { get; set; }
    public User? Owner { get; set; }
    public string OriginalName { get; set; } = default!;
    public string Ext { get; set; } = default!;
    public string Mime { get; set; } = default!;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = default!;
    public string StoragePublicId { get; set; } = default!;
    public string StorageResourceType { get; set; } = "raw"; // raw|image|video
    public string? PreviewPublicId { get; set; }
    public int? PreviewPages { get; set; }
    public string? ThumbnailUrl { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public string? ProcessingError { get; set; }
    public short ProcessingAttempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Ảnh nhúng trong câu hỏi của quiz (import Word) — quyền xem = quyền xem quiz chứa nó.</summary>
    public long? QuizId { get; set; }
    public Quiz? Quiz { get; set; }
}

public class Document
{
    public long Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Summary { get; set; }
    public string? DescriptionHtml { get; set; }
    public long? SectionId { get; set; }
    public Section? Section { get; set; }
    public short? GradeId { get; set; }
    public Grade? Grade { get; set; }
    public long? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public long? SchoolYearId { get; set; }
    public SchoolYear? SchoolYear { get; set; }
    public short? WeekNo { get; set; }
    public long? ClassId { get; set; }
    public ClassEntity? Class { get; set; }
    public long? OwnerId { get; set; }
    public User? Owner { get; set; }
    public long? TeamId { get; set; }
    public Team? Team { get; set; }
    public ContentScope Scope { get; set; } = ContentScope.Public;
    public PublishMode PublishMode { get; set; } = PublishMode.Visible;
    public DateTimeOffset? PublishFrom { get; set; }
    public DateTimeOffset? PublishUntil { get; set; }
    public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Approved;
    public string? ModerationNote { get; set; }
    public bool AllowGuestDownload { get; set; }
    public long? CoverFileId { get; set; }
    public FileEntity? CoverFile { get; set; }
    public bool IsFeatured { get; set; }
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Computed column (f_unaccent(lower(title+summary))) — tìm kiếm không dấu §9, decisions.md M3.</summary>
    public string? SearchText { get; set; }

    public ICollection<DocumentFile> Files { get; set; } = [];
    public ICollection<DocumentTag> Tags { get; set; } = [];
}

public class DocumentFile
{
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;
    public long FileId { get; set; }
    public FileEntity File { get; set; } = default!;
    public short Sort { get; set; }
}

public class DocumentTag
{
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;
    public long TagId { get; set; }
    public Tag Tag { get; set; } = default!;
}
