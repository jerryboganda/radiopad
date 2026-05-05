using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Generic OpenAI-API-compatible adapter for tenant-supplied endpoints
/// (DigitalOcean serverless inference, Nvidia NIM, Cloudflare AI, Together,
/// Groq, vLLM, Mistral, OpenRouter, …). Reads <c>EndpointUrl</c>,
/// <c>Model</c>, <c>ApiKeySecretRef</c> from the
/// <see cref="Domain.Entities.ProviderConfig"/> row.
///
/// <para>The runtime compliance class is whatever the operator stored on the
/// row (<see cref="Domain.Entities.ProviderConfig.Compliance"/>); the AI
/// gateway uses that — adapter just exposes a sensible default for catalog
/// seeding via <see cref="ResolveDefaultComplianceClass"/>:
/// <list type="bullet">
///   <item>Localhost-style hosts (<c>127.0.0.1</c>, <c>localhost</c>, <c>*.local</c>) &#8594; <see cref="ProviderComplianceClass.LocalOnly"/>.</item>
///   <item>Everything else &#8594; <see cref="ProviderComplianceClass.Sandbox"/> (operators must opt-in to PhiApproved per BAA).</item>
/// </list>
/// </para>
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProviderAdapter
{
    public const string AdapterId = "openai-compatible";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<OpenAiCompatibleProvider> _log;

    public OpenAiCompatibleProvider(IHttpClientFactory http, ILogger<OpenAiCompatibleProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        if (string.IsNullOrWhiteSpace(p.EndpointUrl))
            throw new ProviderTransportException($"{AdapterId}: provider '{p.Name}' is missing an endpoint URL.");

        var baseUrl = p.EndpointUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(p.Model) ? "default" : p.Model;
        var url = OpenAiChatHelpers.NormalizeChatCompletionsUrl(baseUrl);

        var client = _http.CreateClient("ai");
        var apiKey = ProviderSecretResolver.Resolve(p.ApiKeySecretRef);
        // Iter-36 — when the operator has registered a secret reference
        // (e.g. <c>env:OPENAI_API_KEY</c>) but the env var is not present at
        // runtime, fail fast with a policy exception instead of letting the
        // upstream return a confusing 401. An empty <c>ApiKeySecretRef</c>
        // is still allowed (e.g. for vLLM behind an OpenAI-compatible shim
        // that does not require auth).
        if (!string.IsNullOrEmpty(p.ApiKeySecretRef) && string.IsNullOrEmpty(apiKey))
            throw new ProviderPolicyException($"{AdapterId}: api_key_missing for secret ref '{p.ApiKeySecretRef}'.");
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = OpenAiChatHelpers.BuildChatBody(model, request.SystemPrompt, request.UserPrompt);
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

    /// <summary>
    /// Default compliance class for catalog seeding — see class summary.
    /// </summary>
    public static ProviderComplianceClass ResolveDefaultComplianceClass(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl)) return ProviderComplianceClass.Sandbox;
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri)) return ProviderComplianceClass.Sandbox;
        var host = uri.Host;
        if (string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderComplianceClass.LocalOnly;
        }
        return ProviderComplianceClass.Sandbox;
    }
}
