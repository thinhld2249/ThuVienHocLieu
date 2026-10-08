namespace HocLieu.Infrastructure.Conversion;

/// <summary>Chuyển Office (doc/docx/xls/xlsx/ppt/pptx) → PDF (spec §11.2).</summary>
public interface IDocumentConverter
{
    /// <summary>Trả về bytes PDF. Lỗi (timeout, file hỏng…) → throw, worker retry.</summary>
    Task<byte[]> ConvertToPdfAsync(string fileName, byte[] content, CancellationToken ct);
}

/// <summary>Gotenberg 8 — <c>POST /forms/libreoffice/convert</c>, field <c>files</c>, 1 file → PDF (spec §11.4).</summary>
public sealed class GotenbergConverter(
    IHttpClientFactory httpFactory,
    ILogger<GotenbergConverter> logger) : IDocumentConverter
{
    public async Task<byte[]> ConvertToPdfAsync(string fileName, byte[] content, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("gotenberg");
        using var multipart = new MultipartFormDataContent();
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            Infrastructure.Storage.FileTypes.MimeOf(ext));
        multipart.Add(fileContent, "files", fileName);

        using var response = await client.PostAsync("/forms/libreoffice/convert", multipart, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Gotenberg trả về {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        }

        var pdf = await response.Content.ReadAsByteArrayAsync(ct);
        logger.LogDebug("Đã chuyển {File} ({Bytes} bytes) → PDF {PdfBytes} bytes", fileName, content.Length, pdf.Length);
        return pdf;
    }
}
