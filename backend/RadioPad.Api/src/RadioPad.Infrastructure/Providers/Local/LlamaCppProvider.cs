using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Iter-32 AI-011 — llama.cpp HTTP server adapter. Targets
/// <c>POST {base}/completion</c> which is the canonical endpoint for the
/// <c>llama-server</c> binary that ships with llama.cpp. Default endpoint
/// is <c>http://127.0.0.1:8080</c>; compliance class defaults to
/// <see cref="ProviderComplianceClass.LocalOnly"/>.
///
/// <para>The system + user prompt are concatenated into the
/// <c>prompt</c> field with a <c>SYSTEM:</c> / <c>USER:</c> framing because
/// llama.cpp's <c>/completion</c> is a single-prompt endpoint; callers that
/// need true chat-style messaging should configure the OpenAI-compatible
/// adapter against llama.cpp's <c>/v1/chat/completions</c> route instead.</para>
/// </summary>
public sealed class LlamaCppProvider : IAiProviderAdapter
{
    public const string AdapterId = "llama-cpp";
    public const string DefaultEndpoint = "http://127.0.0.1:8080";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.LocalOnly;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<LlamaCppProvider> _log;

    public LlamaCppProvider(IHttpClientFactory http, ILogger<LlamaCppProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var baseUrl = string.IsNullOrWhiteSpace(p.EndpointUrl) ? DefaultEndpoint : p.EndpointUrl.TrimEnd('/');
        var url = $"{baseUrl}/completion";
        var prompt = $"SYSTEM: {request.SystemPrompt}\n\nUSER: {request.UserPrompt}\n\nASSISTANT:";

        // Brief §5.4 — forward the clamped temperature (callers set ≈0 for MedGemma report
        // formatting) and, when supplied, the GBNF grammar so decoding is structurally constrained.
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["n_predict"] = 1024,
            ["stream"] = false,
            ["temperature"] = request.Temperature,
        };
        if (!string.IsNullOrWhiteSpace(request.Grammar))
            body["grammar"] = request.Grammar;

        // Dedicated client (Program.cs "ai-local"): CPU-bound local inference needs minutes, not
        // the cloud-tuned "ai" client's ~60 s attempt budget, and must not share its circuit breaker.
        var client = _http.CreateClient("ai-local");
        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync(url, body, cancellationToken);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            throw new ProviderTransportException($"{AdapterId}: HTTP transport failure: {hre.Message}", inner: hre);
        }
        catch (TaskCanceledException tce) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            throw new ProviderTransportException($"{AdapterId}: request timed out after {sw.ElapsedMilliseconds} ms", inner: tce);
        }

        try
        {
            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await OpenAiChatHelpers.SafeReadAsync(resp, cancellationToken);
                throw new ProviderTransportException(
                    $"{AdapterId}: upstream returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).",
                    statusCode: (int)resp.StatusCode,
                    responseBody: bodyText);
            }

            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            sw.Stop();

            var root = doc.RootElement;
            var text = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            // llama.cpp returns timing as "tokens_evaluated" (prompt) and "tokens_predicted" (completion).
            var pt = root.TryGetProperty("tokens_evaluated", out var te) && te.ValueKind == JsonValueKind.Number ? te.GetInt32() : 0;
            var ctok = root.TryGetProperty("tokens_predicted", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 0;

            return new AiResult(
                Text: text,
                Provider: p.Name,
                Model: string.IsNullOrWhiteSpace(p.Model) ? "llama-cpp" : p.Model,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                InputTokens: pt,
                OutputTokens: ctok,
                PromptVersion: request.PromptVersion);
        }
        finally
        {
            resp.Dispose();
        }
    }

    /// <summary>Probe <c>GET {base}/health</c> (returns <c>{"status":"ok"}</c>).</summary>
    public async Task<(bool ok, string? error)> ProbeAsync(string? endpointUrl, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpoint : endpointUrl.TrimEnd('/');
        try
        {
            var client = _http.CreateClient("ai-local");
            using var resp = await client.GetAsync($"{baseUrl}/health", ct);
            return resp.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
