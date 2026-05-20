using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;

namespace RadioPad.Infrastructure.Email;

/// <summary>
/// Sends transactional emails via an HTTPS REST API (bypasses DigitalOcean SMTP
/// block). Supports Resend (default), SendGrid, and Mailgun. Falls back to SMTP
/// only when RADIOPAD_EMAIL_PROVIDER is "smtp" or not set and an SMTP host is
/// configured.
///
/// Environment variables:
///   RADIOPAD_EMAIL_PROVIDER  = resend | sendgrid | mailgun | smtp
///   RADIOPAD_EMAIL_API_KEY   = API key for the chosen provider (env:NAME pattern)
///   RADIOPAD_EMAIL_FROM      = Default From address (e.g. "RadioPad noreply@radiopad.polytronx.com")
///   RADIOPAD_EMAIL_REPLY_TO  = Optional Reply-To address
///
/// For Mailgun, also set:
///   RADIOPAD_MAILGUN_DOMAIN  = Your sending domain (e.g. mg.radiopad.polytronx.com)
/// </summary>
public sealed class HttpEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpEmailSender> _log;

    public HttpEmailSender(HttpClient http, ILogger<HttpEmailSender> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var provider = Environment.GetEnvironmentVariable("RADIOPAD_EMAIL_PROVIDER")?.Trim().ToLowerInvariant() ?? "resend";
        var apiKey = Environment.GetEnvironmentVariable("RADIOPAD_EMAIL_API_KEY") ?? "";
        var defaultFrom = Environment.GetEnvironmentVariable("RADIOPAD_EMAIL_FROM") ?? "RadioPad <noreply@radiopad.polytronx.com>";
        var defaultReplyTo = Environment.GetEnvironmentVariable("RADIOPAD_EMAIL_REPLY_TO");

        var from = message.From ?? defaultFrom;
        var replyTo = message.ReplyTo ?? defaultReplyTo;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogError("RADIOPAD_EMAIL_API_KEY not set — cannot send email via {Provider}.", provider);
            return false;
        }

        try
        {
            return provider switch
            {
                "resend" => await SendViaResendAsync(apiKey, from, replyTo, message, ct),
                "sendgrid" => await SendViaSendGridAsync(apiKey, from, replyTo, message, ct),
                "mailgun" => await SendViaMailgunAsync(apiKey, from, replyTo, message, ct),
                _ => throw new InvalidOperationException($"Unknown email provider: {provider}. Use resend, sendgrid, or mailgun.")
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Email send failed via {Provider} to {To}.", provider, message.To);
            return false;
        }
    }

    private async Task<bool> SendViaResendAsync(string apiKey, string from, string? replyTo, EmailMessage msg, CancellationToken ct)
    {
        // Resend API: POST https://api.resend.com/emails
        var payload = new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = new[] { msg.To },
            ["subject"] = msg.Subject,
            ["html"] = msg.HtmlBody,
        };
        if (!string.IsNullOrWhiteSpace(msg.PlainBody))
            payload["text"] = msg.PlainBody;
        if (!string.IsNullOrWhiteSpace(replyTo))
            payload["reply_to"] = replyTo;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            _log.LogInformation("Email sent via Resend to {To}, subject={Subject}.", msg.To, msg.Subject);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Resend API returned {Status}: {Body}", (int)response.StatusCode, body);
        return false;
    }

    private async Task<bool> SendViaSendGridAsync(string apiKey, string from, string? replyTo, EmailMessage msg, CancellationToken ct)
    {
        // SendGrid v3 API: POST https://api.sendgrid.com/v3/mail/send
        var content = new List<Dictionary<string, object>>
        {
            new()
            {
                ["type"] = "text/html",
                ["value"] = msg.HtmlBody
            }
        };
        if (!string.IsNullOrWhiteSpace(msg.PlainBody))
        {
            content.Insert(0, new Dictionary<string, object>
            {
                ["type"] = "text/plain",
                ["value"] = msg.PlainBody
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["personalizations"] = new[]
            {
                new { to = new[] { new { email = msg.To } } }
            },
            ["from"] = ParseEmailAddress(from),
            ["subject"] = msg.Subject,
            ["content"] = content
        };
        if (!string.IsNullOrWhiteSpace(replyTo))
            payload["reply_to"] = ParseEmailAddress(replyTo);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 202)
        {
            _log.LogInformation("Email sent via SendGrid to {To}.", msg.To);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        _log.LogWarning("SendGrid API returned {Status}: {Body}", (int)response.StatusCode, body);
        return false;
    }

    private async Task<bool> SendViaMailgunAsync(string apiKey, string from, string? replyTo, EmailMessage msg, CancellationToken ct)
    {
        // Mailgun API: POST https://api.mailgun.net/v3/{domain}/messages
        var domain = Environment.GetEnvironmentVariable("RADIOPAD_MAILGUN_DOMAIN") ?? "mg.radiopad.polytronx.com";

        var formData = new MultipartFormDataContent
        {
            { new StringContent(from), "from" },
            { new StringContent(msg.To), "to" },
            { new StringContent(msg.Subject), "subject" },
            { new StringContent(msg.HtmlBody), "html" },
        };
        if (!string.IsNullOrWhiteSpace(msg.PlainBody))
            formData.Add(new StringContent(msg.PlainBody), "text");
        if (!string.IsNullOrWhiteSpace(replyTo))
            formData.Add(new StringContent(replyTo), "h:Reply-To");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.mailgun.net/v3/{domain}/messages");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = formData;

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            _log.LogInformation("Email sent via Mailgun to {To}.", msg.To);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        _log.LogWarning("Mailgun API returned {Status}: {Body}", (int)response.StatusCode, body);
        return false;
    }

    private static object ParseEmailAddress(string raw)
    {
        // Parse "Display Name <email@domain>" or plain "email@domain"
        var trimmed = raw.Trim();
        var angleBracket = trimmed.IndexOf('<');
        if (angleBracket > 0 && trimmed.EndsWith('>'))
        {
            var name = trimmed[..angleBracket].Trim().Trim('"');
            var email = trimmed[(angleBracket + 1)..^1].Trim();
            return new { email, name };
        }
        return new { email = trimmed };
    }
}
