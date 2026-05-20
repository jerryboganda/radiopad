using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// OpenAI api.openai.com adapter. Default compliance class is
/// <see cref="ProviderComplianceClass.NonPhi"/> equivalent — i.e.
/// <see cref="ProviderComplianceClass.Sandbox"/> — because OpenAI requires a
/// separate BAA / Zero-Data-Retention amendment before PHI may be routed.
/// Tenants with a signed BAA must explicitly flip the provider row to
/// <see cref="ProviderComplianceClass.PhiApproved"/>; the AI gateway never
/// promotes compliance automatically.
/// </summary>
public sealed class OpenAiDirectProvider : IAiProviderAdapter
{
    public const string AdapterId = "openai";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.Sandbox;
    private const string DefaultBaseUrl = "https://api.openai.com";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<OpenAiDirectProvider> _log;

    public OpenAiDirectProvider(IHttpClientFactory http, ILogger<OpenAiDirectProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var apiKey = ProviderSecretResolver.Resolve(p.ApiKeySecretRef, fallbackEnv: "RADIOPAD_PROVIDER_OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new ProviderTransportException($"{AdapterId}: API key secret '{p.ApiKeySecretRef}' did not resolve.");

        var baseUrl = string.IsNullOrWhiteSpace(p.EndpointUrl) ? DefaultBaseUrl : p.EndpointUrl.TrimEnd('/');
        OpenAiChatHelpers.ValidateHostedEndpoint(baseUrl, AdapterId);
        var model = string.IsNullOrWhiteSpace(p.Model) ? "gpt-4o-mini" : p.Model;
        var url = OpenAiChatHelpers.NormalizeChatCompletionsUrl(baseUrl);

        var client = _http.CreateClient("ai");
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
}
