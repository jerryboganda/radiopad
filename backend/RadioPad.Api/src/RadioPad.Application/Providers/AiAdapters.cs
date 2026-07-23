using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Providers;

/// <summary>
/// Mock adapter — returns a deterministic templated impression. Used in
/// dev, in tests, and as the default fallback when no other adapter is
/// configured. Never charges, never leaves the process.
/// </summary>
public class MockAiAdapter : IAiProviderAdapter
{
    public string Id => "mock";

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var bullets = new[]
        {
            "1. No acute intracranial abnormality identified on the supplied input.",
            "2. Findings as described above; recommend correlation with clinical history.",
            "3. No measurement-impacting change versus prior comparison.",
        };
        var text = string.Join('\n', bullets);

        // AI-013 — when a streaming sink is attached, emit a deterministic 3-chunk synthetic
        // stream so streaming integration/coordinator tests have observable, ordered progress
        // without a network provider. The pieces concatenate back to exactly `text`.
        if (request.OnStream is not null)
        {
            // Test-support only: a provider whose Model is "mock-slow" (case-insensitive prefix)
            // spaces the chunks out so a real async job stays "running" long enough for a poll to
            // observe live progress. Production mock models ("mock-1", null, …) stream instantly.
            var delayMs = request.Provider.Model?.StartsWith("mock-slow", StringComparison.OrdinalIgnoreCase) == true ? 60 : 0;
            var tokens = 0;
            foreach (var piece in ThreeChunks(text))
            {
                tokens++;
                request.OnStream.Report(new AiStreamChunk(piece, tokens));
                if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            }
        }

        return new AiResult(
            Text: text,
            Provider: request.Provider.Name,
            Model: string.IsNullOrEmpty(request.Provider.Model) ? "mock-1" : request.Provider.Model,
            LatencyMs: 5,
            InputTokens: request.UserPrompt.Length / 4,
            OutputTokens: text.Length / 4,
            PromptVersion: request.PromptVersion);
    }

    private static IEnumerable<string> ThreeChunks(string text)
    {
        if (text.Length == 0) { yield return string.Empty; yield break; }
        var size = (int)Math.Ceiling(text.Length / 3.0);
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}

/// <summary>
/// Anthropic adapter (Messages API). Reads the API key from an environment
/// variable referenced by <see cref="ProviderConfig.ApiKeySecretRef"/>.
/// </summary>
public class AnthropicAiAdapter : IAiProviderAdapter
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<AnthropicAiAdapter> _log;

    public AnthropicAiAdapter(IHttpClientFactory http, ILogger<AnthropicAiAdapter> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => "anthropic";

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var apiKey = ResolveSecret(request.Provider.ApiKeySecretRef);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                $"Anthropic API key is not configured (secret ref '{request.Provider.ApiKeySecretRef}').");

        var client = _http.CreateClient("anthropic");
        client.BaseAddress ??= new Uri("https://api.anthropic.com/");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Remove("anthropic-version");
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var model = string.IsNullOrEmpty(request.Provider.Model) ? "claude-3-5-sonnet-latest" : request.Provider.Model;
        var messages = new[] { new { role = "user", content = request.UserPrompt } };

        // AI-013 — stream only when a sink is attached; otherwise the request body and parsing
        // are byte-identical to the pre-streaming path.
        if (request.OnStream is not null)
            return await CompleteStreamingAsync(client, model, request.SystemPrompt, request, cancellationToken);

        var body = new
        {
            model,
            max_tokens = 1024,
            system = request.SystemPrompt,
            messages,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var resp = await client.PostAsJsonAsync("v1/messages", body, cancellationToken);
        sw.Stop();
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text").GetString() ?? "";
        var inputTokens = doc.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("input_tokens", out var inT) ? inT.GetInt32() : 0;
        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outT) ? outT.GetInt32() : 0;

        return new AiResult(
            Text: text,
            Provider: request.Provider.Name,
            Model: model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            PromptVersion: request.PromptVersion);
    }

    /// <summary>
    /// AI-013 — Anthropic Messages streaming (<c>stream: true</c>). Parses the Anthropic SSE:
    /// <c>message_start</c> → <c>usage.input_tokens</c>; <c>content_block_delta</c> with
    /// <c>delta.type == "text_delta"</c> → a text piece; <c>message_delta</c> → cumulative
    /// <c>usage.output_tokens</c>. Per-piece progress reports a running chunk count; the final
    /// output-token count uses <c>message_delta</c> when present, else the chunk count.
    /// </summary>
    private async Task<AiResult> CompleteStreamingAsync(
        HttpClient client, string model, string systemPrompt,
        AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = request.UserPrompt } },
            stream = true,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(body) };
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        var sb = new StringBuilder();
        var chunkCount = 0;
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var (evt, data) in SseStreamReader.ReadAsync(stream, cancellationToken))
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : evt;
            switch (type)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out var u1) &&
                        u1.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number)
                        inputTokens = it.GetInt32();
                    break;
                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta" &&
                        d.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                    {
                        var piece = txt.GetString() ?? "";
                        if (piece.Length > 0)
                        {
                            sb.Append(piece);
                            chunkCount++;
                            request.OnStream!.Report(new AiStreamChunk(piece, chunkCount));
                        }
                    }
                    break;
                case "message_delta":
                    if (root.TryGetProperty("usage", out var u2) &&
                        u2.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number)
                        outputTokens = ot.GetInt32();
                    break;
            }
        }
        sw.Stop();

        return new AiResult(
            Text: sb.ToString(),
            Provider: request.Provider.Name,
            Model: model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            InputTokens: inputTokens,
            OutputTokens: outputTokens > 0 ? outputTokens : chunkCount,
            PromptVersion: request.PromptVersion);
    }

    private static string? ResolveSecret(string secretRef)
    {
        if (string.IsNullOrEmpty(secretRef)) return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(secretRef[4..]);
        return secretRef;
    }
}

/// <summary>
/// Local model adapter targeting an Ollama-compatible HTTP endpoint
/// (default <c>http://localhost:11434/api/generate</c>). Compliance class
/// should be set to <c>LocalOnly</c>.
/// </summary>
public class OllamaAiAdapter : IAiProviderAdapter
{
    private readonly IHttpClientFactory _http;
    public OllamaAiAdapter(IHttpClientFactory http) => _http = http;
    public string Id => "ollama";

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var endpoint = string.IsNullOrEmpty(request.Provider.EndpointUrl)
            ? "http://localhost:11434/api/generate"
            : request.Provider.EndpointUrl;

        var model = string.IsNullOrEmpty(request.Provider.Model) ? "llama3.1:8b-instruct" : request.Provider.Model;
        var prompt = $"{request.SystemPrompt}\n\n{request.UserPrompt}";
        var streaming = request.OnStream is not null;
        var body = new { model, prompt, stream = streaming };

        var client = _http.CreateClient("ollama");

        // AI-013 — Ollama streams NDJSON (one JSON object per line, NOT SSE): each line's
        // `response` is a delta; the final `done: true` line carries prompt_eval_count /
        // eval_count. Non-streaming path stays byte-identical (`stream: false`).
        if (streaming)
        {
            var swS = System.Diagnostics.Stopwatch.StartNew();
            using var reqS = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
            using var respS = await client.SendAsync(reqS, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            respS.EnsureSuccessStatusCode();

            using var streamS = await respS.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(streamS, Encoding.UTF8);
            var sb = new StringBuilder();
            var chunkCount = 0;
            var promptTokens = 0;
            var evalTokens = 0;

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.Length == 0) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("response", out var rr) && rr.ValueKind == JsonValueKind.String)
                {
                    var piece = rr.GetString() ?? "";
                    if (piece.Length > 0)
                    {
                        sb.Append(piece);
                        chunkCount++;
                        request.OnStream!.Report(new AiStreamChunk(piece, chunkCount));
                    }
                }
                if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
                {
                    if (root.TryGetProperty("prompt_eval_count", out var pe) && pe.ValueKind == JsonValueKind.Number) promptTokens = pe.GetInt32();
                    if (root.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number) evalTokens = ec.GetInt32();
                }
            }
            swS.Stop();

            return new AiResult(
                Text: sb.ToString(),
                Provider: request.Provider.Name,
                Model: model,
                LatencyMs: (int)swS.ElapsedMilliseconds,
                InputTokens: promptTokens,
                OutputTokens: evalTokens > 0 ? evalTokens : chunkCount,
                PromptVersion: request.PromptVersion);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var resp = await client.PostAsJsonAsync(endpoint, body, cancellationToken);
        sw.Stop();
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var text = json.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
        return new AiResult(
            Text: text,
            Provider: request.Provider.Name,
            Model: model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            InputTokens: request.UserPrompt.Length / 4,
            OutputTokens: text.Length / 4,
            PromptVersion: request.PromptVersion);
    }
}
