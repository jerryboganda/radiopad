using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Providers;
using RadioPad.Application.Services;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Helpers shared by every OpenAI-compatible adapter (Azure OpenAI, OpenAI
/// direct, generic OpenAI-compatible). Centralises the chat-completions
/// request body, response parsing, and HTTP-error mapping so each adapter
/// stays a thin shim.
/// </summary>
public static class OpenAiChatHelpers
{
    public static object BuildChatBody(
        string model,
        string systemPrompt,
        string userPrompt,
        int? maxTokens = null,
        double temperature = AiCompletionRequest.DefaultTemperature,
        string? outputSchema = null,
        bool stream = false)
    {
        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt },
        };

        // Iter-0b (AI-015 / RB-005) — when a rulebook binds a JSON Schema and it
        // parses, request structured output. A malformed schema is ignored
        // (free text) rather than failing the run. AI-013 — json_schema + streaming
        // is supported by OpenAI/Azure/vLLM, so the schema branch streams too.
        if (!string.IsNullOrWhiteSpace(outputSchema) &&
            TryParseSchema(outputSchema, out var schemaElement))
        {
            // stream_options{include_usage:true} makes the server emit a final usage chunk
            // with real prompt/completion token counts; a server that ignores it simply
            // yields no usage and the caller falls back to counting chunks.
            if (stream)
            {
                return new
                {
                    model,
                    messages,
                    temperature,
                    max_tokens = maxTokens ?? 1024,
                    stream = true,
                    stream_options = new { include_usage = true },
                    response_format = new
                    {
                        type = "json_schema",
                        json_schema = new { name = "radiopad_structured_output", schema = schemaElement, strict = true },
                    },
                };
            }

            return new
            {
                model,
                messages,
                // Iter-0b (AI-014) — temperature already clamped to [0, 0.4] upstream.
                temperature,
                max_tokens = maxTokens ?? 1024,
                stream = false,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new { name = "radiopad_structured_output", schema = schemaElement, strict = true },
                },
            };
        }

        if (stream)
        {
            return new
            {
                model,
                messages,
                temperature,
                max_tokens = maxTokens ?? 1024,
                stream = true,
                stream_options = new { include_usage = true },
            };
        }

        return new
        {
            model,
            messages,
            temperature,
            max_tokens = maxTokens ?? 1024,
            stream = false,
        };
    }

    /// <summary>Iter-0b — parse a JSON-Schema string into a JsonElement; false when malformed.</summary>
    private static bool TryParseSchema(string schema, out JsonElement element)
    {
        try
        {
            using var doc = JsonDocument.Parse(schema);
            element = doc.RootElement.Clone();
            return element.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    public static string NormalizeChatCompletionsUrl(string baseOrFullUrl)
    {
        var url = (baseOrFullUrl ?? "").Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return $"{url}/chat/completions";
        return $"{url}/v1/chat/completions";
    }

    public static string NormalizeModelsUrl(string baseOrFullUrl)
    {
        var url = (baseOrFullUrl ?? "").Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url[..^"/chat/completions".Length] + "/models";
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return $"{url}/models";
        return $"{url}/v1/models";
    }

    public static async Task<Uri> ValidateEndpointAsync(string url, bool allowPrivateNetwork, string adapterId, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ProviderPolicyException($"{adapterId}: endpoint_url_invalid");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalHost(uri.Host))
            throw new ProviderPolicyException($"{adapterId}: endpoint_requires_https");

        if (allowPrivateNetwork) return uri;

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            throw new ProviderPolicyException($"{adapterId}: endpoint_host_unresolvable");
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
            throw new ProviderPolicyException($"{adapterId}: endpoint_private_network_blocked");

        return uri;
    }

    public static Uri ValidateHostedEndpoint(string url, string adapterId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ProviderPolicyException($"{adapterId}: endpoint_requires_https");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ProviderPolicyException($"{adapterId}: endpoint_userinfo_blocked");

        if (!uri.IsDefaultPort && uri.Port != 443)
            throw new ProviderPolicyException($"{adapterId}: endpoint_port_blocked");

        if (IPAddress.TryParse(uri.Host, out _))
            throw new ProviderPolicyException($"{adapterId}: endpoint_ip_literal_blocked");

        var host = uri.Host.ToLowerInvariant();
        var allowed = adapterId switch
        {
            OpenAiDirectProvider.AdapterId => host == "api.openai.com",
            AzureOpenAiProvider.AdapterId => host.EndsWith(".openai.azure.com", StringComparison.Ordinal) ||
                                             host.EndsWith(".cognitiveservices.azure.com", StringComparison.Ordinal),
            AwsBedrockProvider.AdapterId => host.StartsWith("bedrock", StringComparison.Ordinal) &&
                                            (host.EndsWith(".amazonaws.com", StringComparison.Ordinal) ||
                                             host.EndsWith(".amazonaws.com.cn", StringComparison.Ordinal)),
            GoogleVertexAiProvider.AdapterId => host == "aiplatform.googleapis.com" ||
                                                host.EndsWith("-aiplatform.googleapis.com", StringComparison.Ordinal),
            _ => true,
        };

        if (!allowed)
            throw new ProviderPolicyException($"{adapterId}: endpoint_host_not_allowed");

        return uri;
    }

    public static async Task ValidateProviderEndpointAsync(
        string adapterId,
        string? endpointUrl,
        bool allowPrivateNetwork,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl)) return;

        if (string.Equals(adapterId, OpenAiCompatibleProvider.AdapterId, StringComparison.OrdinalIgnoreCase))
        {
            await ValidateEndpointAsync(NormalizeChatCompletionsUrl(endpointUrl), allowPrivateNetwork, adapterId, ct);
            return;
        }

        if (IsHostedAdapter(adapterId))
            ValidateHostedEndpoint(endpointUrl, adapterId);
    }

    private static bool IsHostedAdapter(string adapterId) =>
        string.Equals(adapterId, OpenAiDirectProvider.AdapterId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(adapterId, AzureOpenAiProvider.AdapterId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(adapterId, AwsBedrockProvider.AdapterId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(adapterId, GoogleVertexAiProvider.AdapterId, StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
        string.Equals(host, "::1", StringComparison.Ordinal) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] == 0
                || bytes[0] == 127;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes[0] == 0xFC || bytes[0] == 0xFD;
        }

        return true;
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

    /// <summary>
    /// AI-013 — streaming counterpart to <see cref="SendChatAsync"/>. Opens the response with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/>, parses the OpenAI-style SSE
    /// (<c>choices[0].delta.content</c> pieces, skipping role-only/empty deltas), appends each to
    /// the running text, and reports it through <paramref name="onStream"/> with a cumulative
    /// chunk count. Token counts come from the final <c>usage</c> chunk (emitted when the server
    /// honours <c>stream_options.include_usage</c>); a server that ignores it falls back to the
    /// chunk count for completion tokens and 0 for prompt tokens. Error mapping is identical to
    /// <see cref="SendChatAsync"/>; a mid-stream cancellation surfaces as
    /// <see cref="OperationCanceledException"/>, never a transport error.
    /// </summary>
    public static async Task<(string text, int promptTokens, int completionTokens, long elapsedMs)>
        SendChatStreamingAsync(HttpClient client, string url, object body, string adapterId,
                               IProgress<AiStreamChunk> onStream, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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
            var sb = new StringBuilder();
            var chunkCount = 0;
            int? usagePrompt = null;
            int? usageCompletion = null;

            await foreach (var (_, data) in SseStreamReader.ReadAsync(stream, ct))
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // The final usage chunk (stream_options.include_usage) carries real counts.
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    var (pt, comp) = ParseUsage(root);
                    if (pt > 0 || comp > 0) { usagePrompt = pt; usageCompletion = comp; }
                }

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.Object &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    var piece = content.GetString() ?? "";
                    if (piece.Length > 0)
                    {
                        sb.Append(piece);
                        chunkCount++;
                        onStream.Report(new AiStreamChunk(piece, chunkCount));
                    }
                }
            }
            sw.Stop();

            return (sb.ToString(), usagePrompt ?? 0, usageCompletion ?? chunkCount, sw.ElapsedMilliseconds);
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
