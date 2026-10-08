using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HocLieu.Infrastructure.Email;

/// <summary>§3.2: email thông báo (tùy chọn SMTP; không cấu hình → NoopEmailSender chỉ log).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default);
}

public sealed class NoopEmailSender(ILogger<NoopEmailSender> log) : IEmailSender
{
    public Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        log.LogInformation("Email (noop) → {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(
    IConfiguration config,
    ILogger<SmtpEmailSender> log) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        var host = config["Smtp:Host"]!;
        var port = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
        var user = config["Smtp:User"];
        var password = config["Smtp:Password"];
        var from = config["Smtp:From"];

        try
        {
            using var client = new SmtpClient(host, port);
            if (!string.IsNullOrEmpty(user))
            {
                client.Credentials = new NetworkCredential(user, password);
                client.EnableSsl = port == 587;
            }
            await client.SendMailAsync(new MailMessage(from ?? "no-reply@hoclieu.dev", to, subject, bodyHtml)
            {
                IsBodyHtml = true,
            }, ct);
        }
        catch (Exception ex)
        {
            // Email là thông báo phụ — lỗi gửi không được làm hỏng thao tác chính
            log.LogWarning(ex, "Gửi email thất bại → {To}: {Subject}", to, subject);
        }
    }
}
