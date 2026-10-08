using System.Globalization;
using System.Net;
using HocLieu.Common;
using HocLieu.Common.OpenApi;
using HocLieu.Domain;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Features.Admin;

/// <summary>
/// §5.4 /admin/noi-dung (M6): mọi tài liệu &amp; bài tập (kể cả Private, đã xóa) —
/// ẩn/hiện/hẹn giờ, ghim nổi bật, đổi chuyên mục, kiểm duyệt, xóa mềm/khôi phục.
/// </summary>
public static class AdminContentEndpoints
{
    public static IEndpointRouteBuilder MapAdminContentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/content", async (
                AppDbContext db, TimeProvider time,
                [FromQuery] string? kind,
                [FromQuery] long? ownerId, [FromQuery] long? teamId, [FromQuery] long? sectionId,
                [FromQuery] string? q, [FromQuery] string? moderation,
                [FromQuery] bool? deleted,
                [FromQuery] int? page, [FromQuery] int? pageSize,
                CancellationToken ct) =>
            {
                if (kind is not ("document" or "quiz"))
                    return ApiErrors.Validation(new() { ["kind"] = ["kind phải là \"document\" hoặc \"quiz\"."] });
                (var p, var ps) = Pagination.Parse(page, pageSize, 24);
                var now = time.GetUtcNow();
                var text = q?.Trim();

                int total;
                List<AdminContentItemDto> items;
                var isDeleted = deleted ?? false;

                if (kind == "document")
                {
                    IQueryable<Document> query = db.Documents.IgnoreQueryFilters()
                        .Where(d => d.IsDeleted == isDeleted);
                    if (ownerId is not null) query = query.Where(d => d.OwnerId == ownerId);
                    if (teamId is not null) query = query.Where(d => d.TeamId == teamId);
                    if (sectionId is not null) query = query.Where(d => d.SectionId == sectionId);
                    if (text is { Length: > 0 }) query = query.Where(d => d.Title.Contains(text));
                    if (moderation is not null && Enum.TryParse<ModerationStatus>(moderation, true, out var msD))
                        query = query.Where(d => d.ModerationStatus == msD);

                    total = await query.CountAsync(ct);
                    var rows = await query
                        .OrderByDescending(d => d.CreatedAt)
                        .Skip((p - 1) * ps).Take(ps)
                        .Select(d => new DocumentRow(
                            d.Id, d.Title, d.Slug,
                            d.SectionId, d.Section == null ? null : d.Section.Name,
                            d.GradeId, d.Grade == null ? null : d.Grade.Name,
                            d.OwnerId, d.Owner == null ? null : d.Owner.FullName,
                            d.TeamId, d.Team == null ? null : d.Team.Name,
                            d.Scope.ToString(), d.PublishMode.ToString(),
                            d.PublishFrom, d.PublishUntil,
                            d.ModerationStatus.ToString(), d.IsFeatured, d.IsDeleted,
                            d.ViewCount, d.DownloadCount, d.CreatedAt, d.UpdatedAt))
                        .ToListAsync(ct);
                    items = rows.Select(r => new AdminContentItemDto(
                        "document", r.Id, r.Title, r.Slug,
                        r.SectionId, r.SectionName, r.GradeId, r.GradeName,
                        r.OwnerId, r.OwnerName, r.TeamId, r.TeamName,
                        r.Scope, r.PublishMode,
                        Visibility.GetPublishState(ParseMode(r.PublishMode), r.PublishFrom, r.PublishUntil, now).ToString(),
                        r.PublishFrom?.ToString("o"), r.PublishUntil?.ToString("o"),
                        r.ModerationStatus, r.IsFeatured, r.IsDeleted,
                        null, r.ViewCount, r.DownloadCount,
                        r.CreatedAt.ToString("o"), r.UpdatedAt.ToString("o"))).ToList();
                }
                else
                {
                    IQueryable<Quiz> query = db.Quizzes.IgnoreQueryFilters()
                        .Where(qz => qz.IsDeleted == isDeleted);
                    if (ownerId is not null) query = query.Where(qz => qz.OwnerId == ownerId);
                    if (teamId is not null) query = query.Where(qz => qz.TeamId == teamId);
                    if (sectionId is not null) query = query.Where(qz => qz.SectionId == sectionId);
                    if (text is { Length: > 0 }) query = query.Where(qz => qz.Title.Contains(text));
                    if (moderation is not null && Enum.TryParse<ModerationStatus>(moderation, true, out var msQ))
                        query = query.Where(qz => qz.ModerationStatus == msQ);

                    total = await query.CountAsync(ct);
                    var rows = await query
                        .OrderByDescending(qz => qz.CreatedAt)
                        .Skip((p - 1) * ps).Take(ps)
                        .Select(qz => new QuizRow(
                            qz.Id, qz.Title, qz.Slug,
                            qz.SectionId, qz.Section == null ? null : qz.Section.Name,
                            qz.GradeId, qz.Grade == null ? null : qz.Grade.Name,
                            qz.OwnerId, qz.Owner == null ? null : qz.Owner.FullName,
                            qz.TeamId, qz.Team == null ? null : qz.Team.Name,
                            qz.Scope.ToString(), qz.PublishMode.ToString(),
                            qz.PublishFrom, qz.PublishUntil,
                            qz.ModerationStatus.ToString(), qz.IsFeatured, qz.IsDeleted,
                            qz.QuestionCount, qz.AttemptCount, qz.CreatedAt, qz.UpdatedAt))
                        .ToListAsync(ct);
                    items = rows.Select(r => new AdminContentItemDto(
                        "quiz", r.Id, r.Title, r.Slug,
                        r.SectionId, r.SectionName, r.GradeId, r.GradeName,
                        r.OwnerId, r.OwnerName, r.TeamId, r.TeamName,
                        r.Scope, r.PublishMode,
                        Visibility.GetPublishState(ParseMode(r.PublishMode), r.PublishFrom, r.PublishUntil, now).ToString(),
                        r.PublishFrom?.ToString("o"), r.PublishUntil?.ToString("o"),
                        r.ModerationStatus, r.IsFeatured, r.IsDeleted,
                        r.QuestionCount, null, null,
                        r.CreatedAt.ToString("o"), r.UpdatedAt.ToString("o"))).ToList();
                }

                return Results.Ok(new PagedResult<AdminContentItemDto>(items, total, p, ps));
            })
            .WithName("admin.content.list")
            .WithSummary("Mọi nội dung của hệ thống (kể cả Private; lọc GV/tổ/chuyên mục/trạng thái)")
            .RequireAuthorization("Admin")
            .WithMetadata(new QueryParameter("kind"))
            .WithMetadata(new QueryParameter("ownerId", "integer"))
            .WithMetadata(new QueryParameter("teamId", "integer"))
            .WithMetadata(new QueryParameter("sectionId", "integer"))
            .WithMetadata(new QueryParameter("q"))
            .WithMetadata(new QueryParameter("moderation"))
            .WithMetadata(new QueryParameter("deleted", "boolean"))
            .WithMetadata(new QueryParameter("page", "integer"))
            .WithMetadata(new QueryParameter("pageSize", "integer"));

        app.MapPatch("/api/admin/content/{kind}/{id:long}", async (
                string kind, long id, AdminContentPatchRequest req,
                AppDbContext db, TimeProvider time, IAuditLogger audit, CancellationToken ct) =>
            {
                if (req is null)
                    return ApiErrors.Validation(new() { ["body"] = ["Thiếu dữ liệu."] });
                if (kind is not ("document" or "quiz"))
                    return ApiErrors.Validation(new() { ["kind"] = ["kind phải là \"document\" hoặc \"quiz\"."] });

                PublishMode? mode = null;
                if (req.PublishMode is not null)
                {
                    if (!Enum.TryParse<PublishMode>(req.PublishMode, true, out var parsedMode))
                        return ApiErrors.Validation(new() { ["publishMode"] = ["Trạng thái hiển thị không hợp lệ."] });
                    mode = parsedMode;
                }

                ModerationStatus? moderation = null;
                if (req.ModerationStatus is not null)
                {
                    if (!Enum.TryParse<ModerationStatus>(req.ModerationStatus, true, out var parsedMod))
                        return ApiErrors.Validation(new() { ["moderationStatus"] = ["Trạng thái kiểm duyệt không hợp lệ."] });
                    moderation = parsedMod;
                }

                // "" (rỗng) = xóa mốc; ISO = đặt mốc; null (thiếu trường) = giữ nguyên
                DateTimeOffset? publishFrom = req.PublishFrom is { Length: 0 } ? null : ParseInstant(req.PublishFrom);
                DateTimeOffset? publishUntil = req.PublishUntil is { Length: 0 } ? null : ParseInstant(req.PublishUntil);
                if (req.PublishFrom is { Length: > 0 } && publishFrom == null)
                    return ApiErrors.Validation(new() { ["publishFrom"] = ["Thời gian không hợp lệ."] });
                if (req.PublishUntil is { Length: > 0 } && publishUntil == null)
                    return ApiErrors.Validation(new() { ["publishUntil"] = ["Thời gian không hợp lệ."] });
                if (req.SectionId is > 0 && !await db.Sections.AnyAsync(s => s.Id == req.SectionId, ct))
                    return ApiErrors.Validation(new() { ["sectionId"] = ["Chuyên mục không tồn tại."] });

                var now = time.GetUtcNow();
                var changes = new Dictionary<string, object?>();
                try
                {
                    if (kind == "document")
                    {
                        var d = await db.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct)
                            ?? throw new AdminFlowException(404, "not_found", "Không tìm thấy tài liệu.");
                        ApplyDocumentPatch(d, req, mode, publishFrom, publishUntil, moderation, now, changes);
                    }
                    else
                    {
                        var q = await db.Quizzes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct)
                            ?? throw new AdminFlowException(404, "not_found", "Không tìm thấy bài tập.");
                        ApplyQuizPatch(q, req, mode, publishFrom, publishUntil, moderation, now, changes);
                    }

                    await db.SaveChangesAsync(ct);
                    await audit.LogAsync("admin.content.update", kind, id.ToString(), changes, ct);
                    return Results.Ok(new { id, kind });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ApiErrors.Problem(HttpStatusCode.Conflict, ErrorCodes.Conflict,
                        "Nội dung vừa được người khác sửa. Hãy tải lại và thử lại.");
                }
                catch (AdminFlowException ex)
                {
                    return AdminUsersEndpoints.AdminFlowResult(ex);
                }
            })
            .WithName("admin.content.patch")
            .WithSummary("Điều khiển nội dung: ẩn/hiện/hẹn giờ, ghim, kiểm duyệt, đổi chuyên mục, xóa/khôi phục")
            .RequireAuthorization("Admin");

        return app;
    }

    /// <summary>Quy tắc chung spec §4.4: Hiện/Ẩn xóa lịch; Scheduled giữ/đặt mốc.</summary>
    private static void ApplyDocumentPatch(
        Document d, AdminContentPatchRequest req,
        PublishMode? mode, DateTimeOffset? publishFrom, DateTimeOffset? publishUntil,
        ModerationStatus? moderation, DateTimeOffset now, Dictionary<string, object?> changes)
    {
        if (req.IsFeatured is not null)
        {
            d.IsFeatured = req.IsFeatured.Value;
            changes["isFeatured"] = req.IsFeatured;
        }
        if (mode is not null)
        {
            d.PublishMode = mode.Value;
            if (mode == PublishMode.Scheduled)
            {
                if (publishFrom is not null) { d.PublishFrom = publishFrom; changes["publishFrom"] = publishFrom; }
                if (publishUntil is not null) { d.PublishUntil = publishUntil; changes["publishUntil"] = publishUntil; }
            }
            else
            {
                if (d.PublishFrom is not null) { d.PublishFrom = null; changes["publishFrom"] = null; }
                if (d.PublishUntil is not null) { d.PublishUntil = null; changes["publishUntil"] = null; }
            }
            changes["publishMode"] = mode.ToString();
        }
        else if (publishFrom is not null)
        {
            d.PublishFrom = publishFrom;
            changes["publishFrom"] = publishFrom;
        }
        else if (publishUntil is not null)
        {
            d.PublishUntil = publishUntil;
            changes["publishUntil"] = publishUntil;
        }
        if (moderation is not null)
        {
            d.ModerationStatus = moderation.Value;
            if (req.ModerationNote is not null)
                d.ModerationNote = req.ModerationNote;
            changes["moderationStatus"] = moderation.ToString();
        }
        if (req.SectionId is not null)
        {
            d.SectionId = req.SectionId;
            changes["sectionId"] = req.SectionId;
        }
        if (req.IsDeleted is not null && req.IsDeleted != d.IsDeleted)
        {
            d.IsDeleted = req.IsDeleted.Value;
            d.DeletedAt = req.IsDeleted.Value ? now : null;
            changes["isDeleted"] = req.IsDeleted;
        }
    }

    private static void ApplyQuizPatch(
        Quiz q, AdminContentPatchRequest req,
        PublishMode? mode, DateTimeOffset? publishFrom, DateTimeOffset? publishUntil,
        ModerationStatus? moderation, DateTimeOffset now, Dictionary<string, object?> changes)
    {
        if (req.IsFeatured is not null)
        {
            q.IsFeatured = req.IsFeatured.Value;
            changes["isFeatured"] = req.IsFeatured;
        }
        if (mode is not null)
        {
            q.PublishMode = mode.Value;
            if (mode == PublishMode.Scheduled)
            {
                if (publishFrom is not null) { q.PublishFrom = publishFrom; changes["publishFrom"] = publishFrom; }
                if (publishUntil is not null) { q.PublishUntil = publishUntil; changes["publishUntil"] = publishUntil; }
            }
            else
            {
                if (q.PublishFrom is not null) { q.PublishFrom = null; changes["publishFrom"] = null; }
                if (q.PublishUntil is not null) { q.PublishUntil = null; changes["publishUntil"] = null; }
            }
            changes["publishMode"] = mode.ToString();
        }
        else if (publishFrom is not null)
        {
            q.PublishFrom = publishFrom;
            changes["publishFrom"] = publishFrom;
        }
        else if (publishUntil is not null)
        {
            q.PublishUntil = publishUntil;
            changes["publishUntil"] = publishUntil;
        }
        if (moderation is not null)
        {
            q.ModerationStatus = moderation.Value; // Quiz không có ModerationNote (chỉ Document)
            changes["moderationStatus"] = moderation.ToString();
        }
        if (req.SectionId is not null)
        {
            q.SectionId = req.SectionId;
            changes["sectionId"] = req.SectionId;
        }
        if (req.IsDeleted is not null && req.IsDeleted != q.IsDeleted)
        {
            q.IsDeleted = req.IsDeleted.Value;
            q.DeletedAt = req.IsDeleted.Value ? now : null;
            changes["isDeleted"] = req.IsDeleted;
        }
    }

    private static PublishMode ParseMode(string s)
        => Enum.TryParse<PublishMode>(s, true, out var m) ? m : PublishMode.Visible;

    private static DateTimeOffset? ParseInstant(string? s)
        => s is { Length: > 0 } && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v)
            ? v
            : null;
}

/// <summary>Hàng thô tài liệu — format chuỗi làm trong bộ nhớ.</summary>
public record DocumentRow(
    long Id, string Title, string Slug,
    long? SectionId, string? SectionName,
    short? GradeId, string? GradeName,
    long? OwnerId, string? OwnerName,
    long? TeamId, string? TeamName,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil,
    string ModerationStatus, bool IsFeatured, bool IsDeleted,
    int ViewCount, int DownloadCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Hàng thô bài tập.</summary>
public record QuizRow(
    long Id, string Title, string Slug,
    long? SectionId, string? SectionName,
    short? GradeId, string? GradeName,
    long? OwnerId, string? OwnerName,
    long? TeamId, string? TeamName,
    string Scope, string PublishMode,
    DateTimeOffset? PublishFrom, DateTimeOffset? PublishUntil,
    string ModerationStatus, bool IsFeatured, bool IsDeleted,
    int QuestionCount, int AttemptCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record AdminContentItemDto(
    string Kind, long Id, string Title, string Slug,
    long? SectionId, string? SectionName,
    short? GradeId, string? GradeName,
    long? OwnerId, string? OwnerName,
    long? TeamId, string? TeamName,
    string Scope, string PublishMode, string PublishState,
    string? PublishFrom, string? PublishUntil,
    string ModerationStatus, bool IsFeatured, bool IsDeleted,
    int? QuestionCount, int? ViewCount, int? DownloadCount,
    string CreatedAt, string UpdatedAt);

public record AdminContentPatchRequest(
    bool? IsFeatured,
    string? PublishMode,
    string? PublishFrom,
    string? PublishUntil,
    string? ModerationStatus,
    string? ModerationNote,
    long? SectionId,
    bool? IsDeleted);
