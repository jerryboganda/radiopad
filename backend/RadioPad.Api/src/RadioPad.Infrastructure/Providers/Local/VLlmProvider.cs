using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Iter-32 AI-011 — vLLM adapter. vLLM exposes an OpenAI-compatible
/// <c>POST {base}/v1/chat/completions</c> surface, so this thin shim reuses
/// <see cref="OpenAiChatHelpers"/>. Default endpoint is
/// <c>http://127.0.0.1:8000</c>; remote vLLM hosts must be configured
/// explicitly. Compliance class defaults to
/// <see cref="ProviderComplianceClass.LocalOnly"/>.
/// </summary>
public sealed class VLlmProvider : IAiProviderAdapter
{
    public const string AdapterId = "vllm";
    public const string DefaultEndpoint = "http://127.0.0.1:8000";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.LocalOnly;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<VLlmProvider> _log;

    public VLlmProvider(IHttpClientFactory http, ILogger<VLlmProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var baseUrl = string.IsNullOrWhiteSpace(p.EndpointUrl) ? DefaultEndpoint : p.EndpointUrl.TrimEnd('/');
        var url = OpenAiChatHelpers.NormalizeChatCompletionsUrl(baseUrl);
        var model = string.IsNullOrWhiteSpace(p.Model) ? "default" : p.Model;

        var client = _http.CreateClient("ai");
        var apiKey = ProviderSecretResolver.Resolve(p.ApiKeySecretRef);
        if (!string.IsNullOrEmpty(p.ApiKeySecretRef) && string.IsNullOrEmpty(apiKey))
            throw new ProviderPolicyException($"{AdapterId}: api_key_missing for secret ref '{p.ApiKeySecretRef}'.");
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = OpenAiChatHelpers.BuildChatBody(model, request.SystemPrompt ?? "", request.UserPrompt ?? "", temperature: request.Temperature, outputSchema: request.OutputSchema);
        var (text, pt, ctok, ms) = await OpenAiChatHelpers.SendChatAsync(client, url, body, AdapterId, cancellationToken);

        return new AiResult(
            Text: text,
            Provider: p.Name,
            Model: model,
            LatencyMs: (int)ms,
            InputTokens: pt,
            OutputTokens: ctok,
            PromptVersion: request.PromptVersion);
    }

    /// <summary>Probe <c>GET {base}/v1/models</c>.</summary>
    public async Task<(bool ok, string? error)> ProbeAsync(string? endpointUrl, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpoint : endpointUrl.TrimEnd('/');
        try
        {
            var client = _http.CreateClient("ai");
            var modelsUrl = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/models"
                : $"{baseUrl}/v1/models";
            using var resp = await client.GetAsync(modelsUrl, ct);
            return resp.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
