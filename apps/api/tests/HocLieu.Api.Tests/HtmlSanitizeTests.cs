using Shouldly;
using HocLieu.Common;
using Xunit;

namespace HocLieu.Api.Tests;

/// <summary>
/// §12 + decisions.md 2026-10-09: sanitize HTML user-input; iframe chỉ giữ khi src
/// thuộc host video cho phép (YouTube/Vimeo/Drive) — chặn clickjacking/leak.
/// </summary>
public class HtmlSanitizeTests
{
    [Fact]
    public void Clean_IframeYoutubeNocookie_Ke()
    {
        var html = "<p>Video bài giảng</p>" +
                   "<iframe src=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ\" width=\"560\" height=\"315\" " +
                   "frameborder=\"0\" allow=\"accelerometer; autoplay\" allowfullscreen title=\"Video\"></iframe>";

        var cleaned = HtmlSanitize.Clean(html);

        cleaned.ShouldContain("youtube-nocookie.com/embed/dQw4w9WgXcQ");
        cleaned.ShouldContain("allowfullscreen");
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://player.vimeo.com/video/76979871")]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/preview")]
    public void Clean_IframeHostChoPhep_Ke(string src)
    {
        HtmlSanitize.Clean($"<iframe src=\"{src}\"></iframe>").ShouldContain(src);
    }

    [Theory]
    [InlineData("https://evil.com/x.html")]
    [InlineData("https://example.com/iframe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Clean_IframeHostKhongChoPhep_Xoa(string src)
    {
        var cleaned = HtmlSanitize.Clean($"<p>trước</p><iframe src=\"{src}\"></iframe><p>sau</p>");

        cleaned.ShouldNotContain("<iframe");
        cleaned.ShouldNotContain(src);
        cleaned.ShouldContain("trước");
        cleaned.ShouldContain("sau");
    }

    [Fact]
    public void Clean_SelfClosingIframe_CungXoaKhiHostKhongChoPhep()
    {
        HtmlSanitize.Clean("<iframe src=\"https://evil.com/x\" />").ShouldNotContain("<iframe");
    }

    [Fact]
    public void Clean_HttpThayBangHttps()
    {
        HtmlSanitize.Clean("<iframe src=\"http://www.youtube.com/embed/abc12345678\"></iframe>")
            .ShouldContain("src=\"https://www.youtube.com/embed/abc12345678\"");
    }

    [Fact]
    public void Clean_TagNgoaiAllowlist_Xoa_KhopTagChoPhep()
    {
        var cleaned = HtmlSanitize.Clean("<script>alert(1)</script><p>A <strong>B</strong> <em>C</em></p><div>div</div>");

        cleaned.ShouldNotContain("<script");
        cleaned.ShouldNotContain("<div");
        cleaned.ShouldContain("<strong>B</strong>");
        cleaned.ShouldContain("<em>C</em>");
    }

    [Fact]
    public void Clean_Trong_Rong()
    {
        HtmlSanitize.Clean(null).ShouldBe(string.Empty);
        HtmlSanitize.Clean("  ").ShouldBe(string.Empty);
    }
}
