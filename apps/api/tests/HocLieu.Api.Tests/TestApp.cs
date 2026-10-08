using HocLieu.Common.Auth;
using HocLieu.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// WebApplicationFactory với connection string trỏ vào PostgreSQL của Testcontainers
/// và MigrateOnStartup=true → app tự migrate + seed khi khởi động (spec §15.2).
/// GoogleTokenValidator thật được thay bằng <see cref="FakeGoogleTokenValidator"/> (spec §16).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _extraSettings;

    /// <summary>Validator giả — test thêm identity vào <see cref="FakeGoogleTokenValidator.Identities"/> rồi gọi /api/auth/google.</summary>
    public FakeGoogleTokenValidator FakeGoogle { get; } = new();

    /// <summary>
    /// Hook test ghi đè dịch vụ (gọi SAU khi app đăng ký hết) — vd stub <c>IDocumentConverter</c>.
    /// Đặt trước lần dùng client đầu tiên (host build lazy).
    /// </summary>
    public Action<IServiceCollection>? ServiceOverrides { get; set; }

    public ApiFactory(string connectionString, Dictionary<string, string?>? extraSettings = null)
    {
        _connectionString = connectionString;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testing: tắt rate limiter (Program.cs) để test đăng nhập nhiều lần
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Db:MigrateOnStartup", "true");
        builder.UseSetting("Auth:GoogleClientId", "test-client-id");
        foreach (var (key, value) in _extraSettings)
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            // §16: thay GoogleTokenValidator thật bằng bản giả.
            // Chỉ dùng instance method (Clear/Add) — không dùng extension RemoveAll
            // do test project pull DI 9.0.0 transitively, extension không resolve được.
            // AddSingleton<TService>(instance): đăng ký theo interface, không theo concrete type.
            var keep = services.Where(d => d.ServiceType != typeof(IGoogleTokenValidator)).ToList();
            services.Clear();
            foreach (var d in keep)
                services.Add(d);
            services.AddSingleton<IGoogleTokenValidator>(FakeGoogle);
            // Dịch vụ đăng ký sau cùng → thắng khi resolve (GetLastService)
            ServiceOverrides?.Invoke(services);
        });
    }
}

/// <summary>
/// Fixture chung: PostgreSQL 17 qua Testcontainers. Docker không khả dụng
/// (không có daemon) → <see cref="DockerAvailable"/> = false, test tự skip (SkipException).
/// <see cref="Client2"/> = "quá trình" thứ hai cùng DB — kiểm tra phiên sống qua restart (keys DataProtection trong PostgreSQL).
/// </summary>
public sealed class TestApp : IAsyncLifetime
{
    private ApiFactory? _factory;
    private ApiFactory? _factory2;
    private PostgreSqlContainer? _db;
    private string? _storageRoot;
    private readonly List<ApiFactory> _extraFactories = [];
    private readonly List<string> _extraStorageRoots = [];

    public bool DockerAvailable { get; private set; } = true;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        try
        {
            _db = new PostgreSqlBuilder("postgres:17").Build();
            await _db.StartAsync();
        }
        catch
        {
            // Docker daemon không chạy / không có quyền → skip thay vì fail
            DockerAvailable = false;
            return;
        }

        ConnectionString = _db.GetConnectionString();
        // Storage local (fallback khi không có Cloudinary) vào thư mục tạm — không rơi vào bin/
        _storageRoot = Path.Combine(Path.GetTempPath(), "hoclieu-tests-" + Guid.NewGuid().ToString("N"));
        _factory = new ApiFactory(ConnectionString, new Dictionary<string, string?>
        {
            ["Storage:LocalRoot"] = _storageRoot,
        });
        // chạm app một lần để chạy migrate + seed (WebApplicationFactory khởi tạo lazy)
        await _factory.CreateDefaultClient().GetAsync("/health/live");
    }

    public async Task DisposeAsync()
    {
        foreach (var f in _extraFactories)
            await f.DisposeAsync();
        if (_factory2 is not null)
            await _factory2.DisposeAsync();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_db is not null)
            await _db.DisposeAsync();
        if (_storageRoot is not null)
            TryDeleteDir(_storageRoot);
        foreach (var root in _extraStorageRoots)
            TryDeleteDir(root);
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // file còn handle mở (worker đang đọc) — bỏ qua, OS dọn khi restart
        }
        catch (UnauthorizedAccessException)
        {
            // bỏ qua
        }
    }

    /// <summary>Factory phụ cùng DB (config khác: AdminEmails, AllowedDomains…). Tự dispose cùng fixture.</summary>
    public ApiFactory CreateFactory(Dictionary<string, string?>? extraSettings = null)
    {
        if (!DockerAvailable)
            throw new InvalidOperationException("Docker không khả dụng — test phải skip");
        var settings = new Dictionary<string, string?>(extraSettings ?? new());
        if (!settings.ContainsKey("Storage:LocalRoot"))
        {
            // Không override → mặc định rơi vào `data/` trong content root app (thư mục bền).
            // DB test tạo mới mỗi class → fileId chạy lại từ 1 → file/preview cũ của run trước
            // cùng đường dẫn gây lẫn nội dung (xét nghiệm 2026-10-08). Root tạm riêng mỗi factory.
            var root = Path.Combine(Path.GetTempPath(), "hoclieu-tests-" + Guid.NewGuid().ToString("N"));
            settings["Storage:LocalRoot"] = root;
            _extraStorageRoots.Add(root);
        }
        var factory = new ApiFactory(ConnectionString, settings);
        _extraFactories.Add(factory);
        return factory;
    }

    // Cache 1 client duy nhất: cookie jar (cookie container) phải sống qua nhiều request.
    // Dùng CreateClient() (HandleCookies=true) — KHÔNG dùng CreateDefaultClient():
    // bản default tạo handler trần, không lưu/đưa cookie theo request → mất phiên.
    private System.Net.Http.HttpClient? _defaultClient;

    public System.Net.Http.HttpClient Client
    {
        get
        {
            if (!DockerAvailable || _factory is null)
                throw new InvalidOperationException("Docker không khả dụng — test phải skip");
            return _defaultClient ??= _factory.CreateClient();
        }
    }

    public FakeGoogleTokenValidator FakeGoogle
    {
        get
        {
            if (!DockerAvailable || _factory is null)
                throw new InvalidOperationException("Docker không khả dụng — test phải skip");
            return _factory.FakeGoogle;
        }
    }

    /// <summary>DbContext của app — test dùng để gieo dữ liệu (tổ, lời mời, setting…) mà chưa có endpoint.</summary>
    public AppDbContext CreateDb()
    {
        if (!DockerAvailable || _factory is null)
            throw new InvalidOperationException("Docker không khả dụng — test phải skip");
        return _factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>Factory thứ hai cùng DB — mô phỏng restart container (spec M1: "restart vẫn giữ phiên").</summary>
    public System.Net.Http.HttpClient Client2
    {
        get
        {
            if (!DockerAvailable || _factory is null || _db is null)
                throw new InvalidOperationException("Docker không khả dụng — test phải skip");
            _factory2 ??= new ApiFactory(_db.GetConnectionString());
            return _factory2.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = false, // cookie cầm tay từ Client1 — chứng minh keys DataProtection dùng chung qua DB
            });
        }
    }
}
