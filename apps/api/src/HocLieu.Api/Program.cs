using System.Security.Claims;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EFCore.NamingConventions;
using HocLieu.Common;
using HocLieu.Common.Auth;
using HocLieu.Common.OpenApi;
using HocLieu.Features.Admin;
using HocLieu.Features.Announcements;
using HocLieu.Features.Assignments;
using HocLieu.Features.Attempts;
using HocLieu.Features.Classes;
using HocLieu.Features.Auth;
using HocLieu.Features.Documents;
using HocLieu.Features.Quizzes;
using HocLieu.Features.Files;
using HocLieu.Features.Home;
using HocLieu.Features.Notifications;
using HocLieu.Features.StaticPages;
using HocLieu.Features.Taxonomy;
using HocLieu.Features.Teams;
using HocLieu.Infrastructure.Conversion;
using HocLieu.Infrastructure.Data;
using HocLieu.Infrastructure.Data.Seed;
using HocLieu.Infrastructure.Email;
using HocLieu.Infrastructure.Jobs;
using HocLieu.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// §11.1: upload tối đa 50MB (setting) — Kestrel nới 60MB, khớp Caddy request_body
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 60L * 1024 * 1024);

// ===== Logging (JSON ra stdout, spec §13) =====
// Cấu hình trong code (không ReadFrom.Configuration): dotnet-ef design-time
// load reflection formatter từ config bị lỗi "Serilog.Formatting.Compact not found"
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();
builder.Host.UseSerilog();

var config = builder.Configuration;
var connectionString = config.GetConnectionString("Default")
    ?? "Host=localhost;Database=hoclieu;Username=hoclieu;Password=hoclieu";

// ===== Data =====
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddMemoryCache();

// Keys DataProtection lưu PostgreSQL → cookie không mất hiệu lực khi restart container (spec §3.4)
builder.Services.AddDataProtection().PersistKeysToDbContext<AppDbContext>();

// ===== Auth =====
builder.Services.AddHttpContextAccessor();
// Scoped: dùng AppDbContext; OnValidatePrincipal resolve qua RequestServices
builder.Services.AddScoped<SessionStampService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TeamsService>();
// Auth__DevFakeGoogle=true (chỉ dev/E2E): nhận idToken `devfake:{sub}:{email}[:{name}]` — spec §16 "đăng nhập giả lập".
// Prod compose không set biến này; nếu bật ngoài ý muốn phải thấy cảnh báo trong log.
if (string.Equals(config["Auth:DevFakeGoogle"], "true", StringComparison.OrdinalIgnoreCase))
{
    // Bọc validator thật: token `devfake:` → giả lập; token khác → Google thật.
    // → dev chạy song song cả 2 đường (decisions.md 2026-10-09).
    builder.Services.AddSingleton<IGoogleTokenValidator>(
        new DevFakeGoogleTokenValidator(new GoogleTokenValidator()));
    Console.Error.WriteLine("CẢNH BÁO: Auth:DevFakeGoogle đang BẬT — chỉ dùng cho dev/E2E, cấm dùng ở prod.");
}
else
{
    builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
}
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// §3.2: email tốt nhất có thể — không cấu hình SMTP thì chỉ ghi log, không bao giờ làm hỏng request
if (!string.IsNullOrWhiteSpace(config["Smtp:Host"]))
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, NoopEmailSender>();

var googleClientId = config["Auth:GoogleClientId"] ?? string.Empty;

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "hl_session";
        o.Cookie.HttpOnly = true;
        // SameAsRequest: prod luôn HTTPS qua Caddy → cookie đánh dấu Secure;
        // dev/test chạy http (localhost, TestServer) → không đánh dấu để trình duyệt/HttpClient lưu được.
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.LoginPath = "/api/auth/google";
        o.AccessDeniedPath = "/api/auth/google";
        o.Events.OnValidatePrincipal = async ctx =>
        {
            if (ctx.Principal?.FindFirst(CurrentUserService.ClaimUid)?.Value is not { Length: > 0 } uidRaw)
                return;
            if (!long.TryParse(uidRaw, out var uid))
            {
                ctx.RejectPrincipal();
                return;
            }
            var stampRaw = ctx.Principal.FindFirst(CurrentUserService.ClaimStamp)?.Value;
            if (!Guid.TryParse(stampRaw, out var stamp))
            {
                ctx.RejectPrincipal();
                return;
            }
            var stamps = ctx.HttpContext.RequestServices.GetRequiredService<SessionStampService>();
            if (!await stamps.IsSessionValidAsync(uid, stamp, ctx.HttpContext.RequestAborted))
                ctx.RejectPrincipal();
        };
        // Cookie handler .NET 8 mặc định luôn 302 về LoginPath — không hợp API JSON.
        // Trình duyệt (full-page nav) → chuyển sang trang /dang-nhap của FE;
        // fetch/XHR (FE API client) → 401 để TanStack Query xử lý.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Headers["Accept"].ToString()
                    .Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.Redirect("/dang-nhap?returnUrl=" +
                    Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString));
            }
            else
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            return Task.FromResult(true);
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.FromResult(true);
        };
    });

builder.Services.AddAuthorization(o =>
{
    // §2.3: policy ActiveTeacher (status = Active), Admin
    o.AddPolicy("ActiveTeacher", p => p.RequireAuthenticatedUser()
        .RequireClaim("status", "Active"));
    o.AddPolicy("Admin", p => p.RequireAuthenticatedUser()
        .RequireClaim("system_role", "Admin"));
});

// ===== OpenAPI + Scalar (dev) =====
// Gói Microsoft.AspNetCore.OpenApi 8.0.x không có AddOpenApi()/MapOpenApi()
// → generator tự viết từ metadata endpoint (Common/OpenApi, decisions.md 2026-10-07)

// ===== Rate limit (spec §8.4) =====
RateLimits.AddPolicies(builder.Services);

// ===== File & job xử lý preview (spec §11; decisions.md M3: LocalDisk fallback) =====
builder.Services.Configure<FileStorageOptions>(config.GetSection("Storage"));
var cloudinaryUrl = config["Cloudinary:Url"] ?? string.Empty;
if (!string.IsNullOrWhiteSpace(cloudinaryUrl))
{
    // cloudName = host của URL cloudinary://key:secret@cloud (spec §15.2)
    var cloudinary = new Cloudinary(cloudinaryUrl);
    var cloudName = new Uri(cloudinaryUrl).Host;
    var cloudRoot = config["Cloudinary:Root"] ?? string.Empty;
    builder.Services.AddSingleton<IFileStorage>(new CloudinaryFileStorage(cloudinary, cloudRoot, cloudName));
}
else
{
    builder.Services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
}
builder.Services.AddScoped<FilesService>();
builder.Services.AddScoped<AppSettingsService>();

// ===== Tài liệu (M3): sanitize mọi HTML người dùng trước khi lưu (spec §12) =====
// Mọi call site gọi HtmlSanitize.Clean() — cấu hình tập trung tại Common/HtmlSanitize.cs
// (tag + thuộc tính + scheme https + allowlist host iframe cho video nhúng)
builder.Services.AddScoped<DocumentsService>();
builder.Services.AddSingleton<FileProcessingQueue>();
builder.Services.AddSingleton<IDocumentConverter, GotenbergConverter>();
builder.Services.AddHostedService<FileProcessingWorker>();

// ===== Bài tập (M4): quiz + lượt làm của khách + dọn attempt bỏ dở =====
builder.Services.AddScoped<QuizzesService>();
builder.Services.AddScoped<AttemptsService>();
builder.Services.AddHostedService<AttemptSweeper>();

// ===== Lớp học & giao bài (M5) =====
builder.Services.AddScoped<ClassesService>();
builder.Services.AddScoped<AssignmentsService>();

// ===== Admin (M6): user mgmt + transfer + rollover năm học =====
builder.Services.AddScoped<AdminService>();

// ===== Health =====
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db", tags: ["ready"])
    .AddCheck<CloudinaryHealthCheck>("cloudinary", tags: ["ready"])
    .AddCheck<GotenbergHealthCheck>("gotenberg", tags: ["ready"]);

var gotenbergUrl = config["Gotenberg:Url"] ?? string.Empty;
builder.Services.AddHttpClient("gotenberg", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60); // §11.4: 60 giây/file
    if (!string.IsNullOrEmpty(gotenbergUrl))
        c.BaseAddress = new Uri(gotenbergUrl);
});

var app = builder.Build();

// ===== Migrate + seed (1 instance, spec §15.2) =====
if (config.GetValue("Db:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    Log.Information("Đã migrate + seed database");
}

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

app.UseSerilogRequestLogging();

// CSRF cho mọi request ghi (spec §3.4)
app.UseMiddleware<CsrfMiddleware>();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// Testing: tắt để integration test đăng nhập nhiều lần không dính 429 (10/phút/IP)
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

// liveness: process chạy được là đủ, không phụ thuộc dependency (spec §13)
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

app.MapOpenApiDocument();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(o =>
    {
        o.WithTitle("Học Liệu API");
    });
}

// ===== Features =====
app.MapAuthEndpoints();
app.MapTaxonomyEndpoints();
app.MapTeamsEndpoints();
app.MapTeamManagementEndpoints();
app.MapAdminTeamEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminDashboardEndpoints();
app.MapAdminContentEndpoints();
app.MapAdminReportEndpoints();
app.MapAdminTaxonomyEndpoints();
app.MapAdminClassEndpoints();
app.MapAdminAnnouncementEndpoints();
app.MapAdminSettingsEndpoints();
app.MapAdminAuditEndpoints();
app.MapAdminSystemEndpoints();
app.MapNotificationEndpoints();
app.MapHomeEndpoints();
app.MapStaticPageEndpoints();
app.MapAnnouncementEndpoints();
app.MapFileEndpoints();
app.MapDocumentEndpoints();
app.MapPublicContentEndpoints();
app.MapQuizEndpoints();
app.MapAttemptEndpoints();
app.MapClassEndpoints();
app.MapAssignmentEndpoints();

app.Run();

// ===== Health checks tuỳ chỉnh =====
public sealed class CloudinaryHealthCheck(IConfiguration config) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var url = config["Cloudinary:Url"] ?? string.Empty;
        if (string.IsNullOrEmpty(url))
            return Task.FromResult(HealthCheckResult.Degraded("Cloudinary chưa cấu hình (Cloudinary__Url trống)"));
        try
        {
            var cloudinary = new Cloudinary(url);
            cloudinary.Ping();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Cloudinary không phản hồi", ex));
        }
    }
}

public sealed class GotenbergHealthCheck(IConfiguration config, IHttpClientFactory httpFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var url = config["Gotenberg:Url"] ?? string.Empty;
        if (string.IsNullOrEmpty(url))
            return HealthCheckResult.Degraded("Gotenberg chưa cấu hình");
        try
        {
            var client = httpFactory.CreateClient("gotenberg");
            var response = await client.GetAsync("/health", ct);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Gotenberg trả về {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Gotenberg không phản hồi", ex);
        }
    }
}
