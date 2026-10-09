using System.Net;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// Nghiệm thu M0: app chạy trên PostgreSQL thật (Testcontainers), migrate + seed tự động,
/// endpoint công khai + OpenAPI + health. Docker không khả dụng → skip (spec §16).
/// </summary>
public class SmokeTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public SmokeTests(TestApp app) => _app = app;

    private System.Net.Http.HttpClient Client
    {
        get
        {
            if (!_app.DockerAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Docker không khả dụng — bỏ qua test tích hợp");
            return _app.Client;
        }
    }

    [Fact]
    public async Task HealthLive_Returns200()
    {
        var response = await Client.GetAsync("/health/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Taxonomy_ReturnsSeededData()
    {
        var response = await Client.GetAsync("/api/public/taxonomy");
        response.EnsureSuccessStatusCode();

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        var sections = json.GetProperty("sections").GetArrayLength();
        var grades = json.GetProperty("grades").GetArrayLength();
        var subjects = json.GetProperty("subjects").GetArrayLength();
        var years = json.GetProperty("schoolYears").GetArrayLength();

        // seed §9: 8 chuyên mục, khối 1–5, 13 môn, 1 năm học (2026-2027 hiện tại)
        // 2026-10-09: "Bài giảng điện tử" tạm ẩn (is_active = false) → taxonomy công khai chỉ còn 7
        sections.ShouldBe(7);
        grades.ShouldBe(5);
        subjects.ShouldBe(13);
        years.ShouldBe(1);

        var currentYear = json.GetProperty("schoolYears")[0];
        currentYear.GetProperty("name").GetString().ShouldBe("2026-2027");
        currentYear.GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Home_ReturnsEmptyStatsAndSections()
    {
        var response = await Client.GetAsync("/api/public/home");
        response.EnsureSuccessStatusCode();

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("stats").GetProperty("documents").GetInt32().ShouldBe(0);
        json.GetProperty("stats").GetProperty("quizzes").GetInt32().ShouldBe(0);
        json.GetProperty("stats").GetProperty("teachers").GetInt32().ShouldBe(0);
        json.GetProperty("openQuizzes").GetArrayLength().ShouldBe(0);
        // Trang chủ chỉ hiện chuyên mục công khai (spec §5.1); ho-so-to là internal,
        // 2026-10-09: bai-giang-dien-tu tạm ẩn → 8 − 1 (internal) − 1 (ẩn) = 6
        json.GetProperty("sections").GetArrayLength().ShouldBe(6);
    }

    [Fact]
    public async Task StaticPage_GioiThieu_ReturnsSeededPage()
    {
        var response = await Client.GetAsync("/api/public/pages/gioi-thieu");
        response.EnsureSuccessStatusCode();

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetProperty("slug").GetString().ShouldBe("gioi-thieu");
        json.GetProperty("title").GetString().ShouldBe("Giới thiệu");
    }

    [Fact]
    public async Task StaticPage_UnknownSlug_Returns404()
    {
        var response = await Client.GetAsync("/api/public/pages/khong-ton-tai");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Announcements_ReturnsEmptyList()
    {
        var response = await Client.GetAsync("/api/public/announcements");
        response.EnsureSuccessStatusCode();

        var json = (JsonElement)JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeof(JsonElement))!;
        json.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task OpenApiDocument_ContainsMappedEndpoints()
    {
        var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var json = (JsonElement)JsonSerializer.Deserialize(body, typeof(JsonElement))!;
        json.GetProperty("openapi").GetString()!.StartsWith("3.").ShouldBeTrue();
        json.GetProperty("info").GetProperty("title").GetString().ShouldBe("Học Liệu API");

        var paths = json.GetProperty("paths");
        paths.TryGetProperty("/api/public/taxonomy", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/public/home", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/public/pages/{slug}", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404()
    {
        var response = await Client.GetAsync("/api/khong-ton-tai");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
