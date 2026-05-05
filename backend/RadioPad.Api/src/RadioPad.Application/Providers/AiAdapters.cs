using System.Net.Http.Json;
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

    public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var bullets = new[]
        {
            "1. No acute intracranial abnormality identified on the supplied input.",
            "2. Findings as described above; recommend correlation with clinical history.",
            "3. No measurement-impacting change versus prior comparison.",
        };
        var text = string.Join('\n', bullets);
        return Task.FromResult(new AiResult(
            Text: text,
            Provider: request.Provider.Name,
            Model: string.IsNullOrEmpty(request.Provider.Model) ? "mock-1" : request.Provider.Model,
            LatencyMs: 5,
            InputTokens: request.UserPrompt.Length / 4,
            OutputTokens: text.Length / 4,
            PromptVersion: request.PromptVersion));
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

        var body = new
        {
            model = string.IsNullOrEmpty(request.Provider.Model) ? "claude-3-5-sonnet-latest" : request.Provider.Model,
            max_tokens = 1024,
            system = request.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = request.UserPrompt },
            },
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
            Model: body.model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
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

        var body = new
        {
            model = string.IsNullOrEmpty(request.Provider.Model) ? "llama3.1:8b-instruct" : request.Provider.Model,
            prompt = $"{request.SystemPrompt}\n\n{request.UserPrompt}",
            stream = false,
        };

        var client = _http.CreateClient("ollama");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var resp = await client.PostAsJsonAsync(endpoint, body, cancellationToken);
        sw.Stop();
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var text = json.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
        return new AiResult(
            Text: text,
            Provider: request.Provider.Name,
            Model: body.model,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            InputTokens: request.UserPrompt.Length / 4,
            OutputTokens: text.Length / 4,
            PromptVersion: request.PromptVersion);
    }
}
