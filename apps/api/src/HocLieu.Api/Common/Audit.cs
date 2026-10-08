using System.Text.Json;
using HocLieu.Domain.Entities;
using HocLieu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HocLieu.Common;

public interface IAuditLogger
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null, object? data = null, CancellationToken ct = default);
}

public sealed class AuditLogger(AppDbContext db, IHttpContextAccessor http, TimeProvider time) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(string action, string? entityType = null, string? entityId = null, object? data = null, CancellationToken ct = default)
    {
        var ctx = http.HttpContext;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = ctx?.User.FindFirst("uid")?.Value is { Length: > 0 } uid ? long.Parse(uid) : null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data is null ? null : JsonSerializer.Serialize(data, JsonOpts),
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}
