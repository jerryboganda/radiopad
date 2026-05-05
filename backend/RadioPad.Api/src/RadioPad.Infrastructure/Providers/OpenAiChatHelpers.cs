using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Helpers shared by every OpenAI-compatible adapter (Azure OpenAI, OpenAI
/// direct, generic OpenAI-compatible). Centralises the chat-completions
/// request body, response parsing, and HTTP-error mapping so each adapter
/// stays a thin shim.
/// </summary>
internal static class OpenAiChatHelpers
{
    public static object BuildChatBody(string model, string systemPrompt, string userPrompt, int? maxTokens = null)
    {
        return new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            max_tokens = maxTokens ?? 1024,
            stream = false,
        };
    }

    public static string NormalizeChatCompletionsUrl(string baseOrFullUrl)
    {
        var url = (baseOrFullUrl ?? "").Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return $"{url}/chat/completions";
        return $"{url}/v1/chat/completions";
    }

    /// <summary>
    /// POST <paramref name="body"/> to <paramref name="url"/> using
    /// <paramref name="client"/>; on a non-2xx response read at most 4 KiB of
    /// the error body and rethrow as <see cref="ProviderTransportException"/>.
    /// </summary>
    public static async Task<(string text, int promptTokens, int completionTokens, long elapsedMs)>
        SendChatAsync(HttpClient client, string url, object body, string adapterId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync(url, body, ct);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            throw new ProviderTransportException($"{adapterId}: HTTP transport failure: {hre.Message}", inner: hre);
        }
        catch (TaskCanceledException tce) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            throw new ProviderTransportException($"{adapterId}: request timed out after {sw.ElapsedMilliseconds} ms", inner: tce);
        }

        try
        {
            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await SafeReadAsync(resp, ct);
                throw new ProviderTransportException(
                    $"{adapterId}: upstream returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).",
                    statusCode: (int)resp.StatusCode,
                    responseBody: bodyText);
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            sw.Stop();

            var root = doc.RootElement;
            var text = "";
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    text = content.GetString() ?? "";
                }
            }

            var (pt, ctok) = ParseUsage(root);
            return (text, pt, ctok, sw.ElapsedMilliseconds);
        }
        finally
        {
            resp.Dispose();
        }
    }

    public static (int promptTokens, int completionTokens) ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return (0, 0);
        var pt = usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
        var ct = usage.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        return (pt, ct);
    }

    public static async Task<string?> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct);
            return read <= 0 ? null : Encoding.UTF8.GetString(buf, 0, read);
        }
        catch
        {
            return null;
        }
    }
}
