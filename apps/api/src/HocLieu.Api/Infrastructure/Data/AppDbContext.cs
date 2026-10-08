using System.Reflection;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HocLieu.Infrastructure.Data;

/// <summary><see cref="IDataProtectionKeyContext"/>: lưu keys DataProtection (cookie) vào PostgreSQL, spec §3.4.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider) : DbContext(options), IDataProtectionKeyContext
{
    private static readonly Dictionary<Type, (PropertyInfo? Created, PropertyInfo? Updated)> TimestampProps =
        typeof(User).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .ToDictionary(
                t => t,
                t => (
                    t.GetProperty("CreatedAt", BindingFlags.Public | BindingFlags.Instance),
                    t.GetProperty("UpdatedAt", BindingFlags.Public | BindingFlags.Instance)));

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuestionGroup> QuestionGroups => Set<QuestionGroup>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<ClassEntity> Classes => Set<ClassEntity>();
    public DbSet<ClassTeacher> ClassTeachers => Set<ClassTeacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<StaticPage> StaticPages => Set<StaticPage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enum -> text
        foreach (var et in b.Model.GetEntityTypes())
        {
            foreach (var p in et.GetProperties())
            {
                if (p.ClrType.IsEnum)
                    p.SetProviderClrType(typeof(string));
            }
        }

        // ===== users =====
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.GoogleSub).IsUnique();
            e.Property(x => x.Email).HasColumnType("citext");
            e.Property(x => x.SystemRole).HasDefaultValue(SystemRole.Teacher);
            e.Property(x => x.Status).HasDefaultValue(UserStatus.Pending);
            e.HasOne(x => x.RequestedTeam).WithMany().HasForeignKey(x => x.RequestedTeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ===== teams =====
        b.Entity<Team>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.SetNull);
            // tối đa 1 Lead mỗi tổ: enforced qua unique partial index trên team_members
        });

        b.Entity<TeamMember>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.UserId });
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.TeamMemberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TeamId, x.Role })
                .IsUnique()
                .HasDatabaseName("ux_team_one_lead")
                .HasFilter("\"role\" = 'Lead'");
        });

        b.Entity<Invitation>(e =>
        {
            e.Property(x => x.Email).HasColumnType("citext");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.Email, x.TeamId })
                .IsUnique()
                .HasDatabaseName("ux_invite_open")
                .HasFilter("\"accepted_at\" IS NULL AND \"revoked_at\" IS NULL");
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Inviter).WithMany().HasForeignKey(x => x.InvitedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AcceptedUser).WithMany().HasForeignKey(x => x.AcceptedUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // ===== danh mục =====
        b.Entity<Grade>(e =>
        {
            e.Property(x => x.Id).HasConversion<short>();
        });

        b.Entity<Subject>(e => e.HasIndex(x => x.Slug).IsUnique());

        b.Entity<Section>(e => e.HasIndex(x => x.Slug).IsUnique());

        b.Entity<SchoolYear>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.IsCurrent)
                .IsUnique()
                .HasDatabaseName("ux_one_current_year")
                .HasFilter("\"is_current\" = TRUE");
        });

        b.Entity<Tag>(e => e.HasIndex(x => x.Slug).IsUnique());

        // ===== files =====
        b.Entity<FileEntity>(e =>
        {
            e.ToTable("files");
            e.HasIndex(x => x.StoragePublicId).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.SetNull);
        });

        // ===== documents =====
        b.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.SectionId, x.GradeId, x.CreatedAt }).HasDatabaseName("ix_docs_browse").IsDescending(false, false, true);
            e.HasIndex(x => new { x.SectionId, x.GradeId }).HasDatabaseName("ix_docs_browse_live").IsDescending(false, true).HasFilter("NOT \"is_deleted\"");
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.TeamId);
            e.HasIndex(x => new { x.PublishMode, x.PublishFrom, x.PublishUntil });
            e.HasQueryFilter(x => !x.IsDeleted);
            e.Property(x => x.UpdatedAt).IsConcurrencyToken();
            e.Property(x => x.WeekNo).HasColumnType("smallint");
            e.Property(x => x.SearchText)
                .HasComputedColumnSql(
                    "f_unaccent(lower(coalesce(\"title\",'') || ' ' || coalesce(\"summary\",'')))",
                    stored: true);
            e.ToTable(t => t.HasCheckConstraint("ck_documents_week", "week_no IS NULL OR (week_no >= 1 AND week_no <= 37)"));
            e.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SchoolYear).WithMany().HasForeignKey(x => x.SchoolYearId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CoverFile).WithMany().HasForeignKey(x => x.CoverFileId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DocumentFile>(e =>
        {
            e.HasKey(x => new { x.DocumentId, x.FileId });
            e.HasOne(x => x.Document).WithMany(d => d.Files).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentTag>(e =>
        {
            e.HasKey(x => new { x.DocumentId, x.TagId });
            e.HasOne(x => x.Document).WithMany(d => d.Tags).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        // ===== quizzes =====
        b.Entity<Quiz>(e =>
        {
            e.HasIndex(x => new { x.SectionId, x.GradeId, x.CreatedAt }).HasDatabaseName("ix_quizzes_browse").IsDescending(false, false, true);
            e.HasIndex(x => new { x.SectionId, x.GradeId }).HasDatabaseName("ix_quizzes_browse_live").IsDescending(false, true).HasFilter("NOT \"is_deleted\"");
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.TeamId);
            e.HasIndex(x => new { x.PublishMode, x.PublishFrom, x.PublishUntil });
            e.HasIndex(x => new { x.WeekNo, x.GradeId }).HasFilter("\"week_no\" IS NOT NULL");
            e.HasQueryFilter(x => !x.IsDeleted);
            e.Property(x => x.UpdatedAt).IsConcurrencyToken();
            e.Property(x => x.WeekNo).HasColumnType("smallint");
            e.Property(x => x.SearchText)
                .HasComputedColumnSql(
                    "f_unaccent(lower(regexp_replace(coalesce(\"title\",'') || ' ' || coalesce(\"description_html\",''), '<[^>]+>', ' ', 'g')))",
                    stored: true);
            e.ToTable(t => t.HasCheckConstraint("ck_quizzes_week", "week_no IS NULL OR (week_no >= 1 AND week_no <= 37)"));
            e.Property(x => x.TotalPoints).HasPrecision(6, 2);
            e.Property(x => x.ImportWarnings).HasColumnType("jsonb");
            e.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SchoolYear).WithMany().HasForeignKey(x => x.SchoolYearId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SourceFile).WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PrintFile).WithMany().HasForeignKey(x => x.PrintFileId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<QuestionGroup>(e =>
        {
            e.HasOne(x => x.Quiz).WithMany(q => q.Groups).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Question>(e =>
        {
            e.HasOne(x => x.Quiz).WithMany(q => q.Questions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Points).HasPrecision(5, 2);
            e.Property(x => x.AcceptedAnswers).IsRequired(false);
        });

        b.Entity<QuestionOption>(e =>
        {
            e.HasOne(x => x.Question).WithMany(q => q.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ===== classes =====
        b.Entity<ClassEntity>(e =>
        {
            e.ToTable("classes");
            e.HasIndex(x => new { x.SchoolYearId, x.Name }).IsUnique().HasFilter("\"school_year_id\" IS NOT NULL");
            e.HasOne(x => x.Grade).WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SchoolYear).WithMany().HasForeignKey(x => x.SchoolYearId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.HomeroomTeacher).WithMany().HasForeignKey(x => x.HomeroomTeacherId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ClassTeacher>(e =>
        {
            e.HasKey(x => new { x.ClassId, x.UserId });
            e.HasOne(x => x.Class).WithMany(c => c.Teachers).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Student>(e =>
        {
            e.HasOne(x => x.Class).WithMany(c => c.Students).HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClassId, x.FullName });
        });

        b.Entity<Assignment>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).IsFixedLength(true).HasMaxLength(6);
            e.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        // ===== attempts =====
        b.Entity<Attempt>(e =>
        {
            e.HasIndex(x => new { x.QuizId, x.SubmittedAt }).HasDatabaseName("ix_attempts_quiz").IsDescending(false, true);
            e.HasIndex(x => new { x.Status, x.ExpiresAt }).HasDatabaseName("ix_attempts_open").HasFilter("\"status\" = 'InProgress'");
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => new { x.AssignmentId, x.StudentId });
            e.Property(x => x.Layout).HasColumnType("jsonb");
            e.Property(x => x.Score).HasPrecision(6, 2);
            e.Property(x => x.Score10).HasColumnName("score_10").HasPrecision(4, 2);
            e.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assignment).WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AttemptAnswer>(e =>
        {
            e.HasKey(x => new { x.AttemptId, x.QuestionId });
            e.Property(x => x.PointsAwarded).HasPrecision(5, 2);
            e.HasOne(x => x.Attempt).WithMany(a => a.Answers).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ===== khác =====
        b.Entity<Favorite>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ItemType, x.ItemId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Announcement>(e =>
        {
            e.HasIndex(x => new { x.Audience, x.PublishAt });
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StaticPage>(e => e.HasKey(x => x.Slug));

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ContentReport>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ResolvedByUser).WithMany().HasForeignKey(x => x.ResolvedBy).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.ActorUserId, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => new { x.Action, x.CreatedAt }).IsDescending(false, true);
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;
            if (!TimestampProps.TryGetValue(entry.Entity.GetType(), out var props))
                continue;
            if (entry.State == EntityState.Added && props.Created is not null)
                props.Created.SetValue(entry.Entity, now);
            if (props.Updated is not null)
                props.Updated.SetValue(entry.Entity, now);
        }
    }
}
