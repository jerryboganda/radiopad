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
/// Iter-32 AI-011 — Ollama chat-API adapter. Targets the modern
/// <c>POST {base}/api/chat</c> endpoint (introduced in Ollama 0.1.14) which
/// accepts a <c>messages</c> array exactly like the OpenAI chat API. The
/// legacy <c>/api/generate</c> adapter (<c>RadioPad.Application.Providers.OllamaAiAdapter</c>)
/// remains in the DI container for backwards compatibility.
///
/// <para>Default endpoint base is <c>http://127.0.0.1:11434</c>; remote
/// hosts must be configured explicitly. Compliance class defaults to
/// <see cref="ProviderComplianceClass.LocalOnly"/> so PHI requests are
/// permitted only when the operator has confirmed the model never leaves
/// the host.</para>
/// </summary>
public sealed class OllamaProvider : IAiProviderAdapter
{
    public const string AdapterId = "ollama-chat";
    public const string DefaultEndpoint = "http://127.0.0.1:11434";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.LocalOnly;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<OllamaProvider> _log;

    public OllamaProvider(IHttpClientFactory http, ILogger<OllamaProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var baseUrl = string.IsNullOrWhiteSpace(p.EndpointUrl) ? DefaultEndpoint : p.EndpointUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/chat";
        var model = string.IsNullOrWhiteSpace(p.Model) ? "llama3.1:8b-instruct" : p.Model;

        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt ?? "" },
                new { role = "user", content = request.UserPrompt ?? "" },
            },
            stream = false,
        };

        var client = _http.CreateClient("ai");
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
            var text = "";
            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString() ?? "";
            }
            // Ollama reports timing in nanoseconds via "prompt_eval_count" / "eval_count" tokens.
            var pt = root.TryGetProperty("prompt_eval_count", out var pe) && pe.ValueKind == JsonValueKind.Number ? pe.GetInt32() : 0;
            var ctok = root.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0;

            return new AiResult(
                Text: text,
                Provider: p.Name,
                Model: model,
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

    /// <summary>
    /// Iter-32 AI-011 — admin-side health probe. Calls <c>GET {base}/api/tags</c>
    /// (lists installed models) which exists on every supported Ollama
    /// version. Returns <c>(true, null)</c> on 2xx; <c>(false, reason)</c> on
    /// any failure. Never throws.
    /// </summary>
    public async Task<(bool ok, string? error)> ProbeAsync(string? endpointUrl, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpoint : endpointUrl.TrimEnd('/');
        try
        {
            var client = _http.CreateClient("ai");
            using var resp = await client.GetAsync($"{baseUrl}/api/tags", ct);
            return resp.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
