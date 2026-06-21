using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Azure OpenAI Service adapter (PRD PROV catalog row <c>azure-openai</c>).
/// Default compliance class is <see cref="ProviderComplianceClass.PhiApproved"/>
/// because Azure offers a Microsoft-signed BAA. Operators register a deployment
/// per-tenant and supply an API key via the
/// <c>RADIOPAD_PROVIDER_AZURE_OPENAI_API_KEY</c> environment variable
/// referenced by <see cref="ProviderConfig.ApiKeySecretRef"/>
/// (e.g. <c>env:RADIOPAD_PROVIDER_AZURE_OPENAI_API_KEY</c>).
/// </summary>
public sealed class AzureOpenAiProvider : IAiProviderAdapter
{
    public const string AdapterId = "azure-openai";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.PhiApproved;
    private const string DefaultApiVersion = "2024-08-01-preview";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<AzureOpenAiProvider> _log;

    public AzureOpenAiProvider(IHttpClientFactory http, ILogger<AzureOpenAiProvider> log)
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
        OpenAiChatHelpers.ValidateHostedEndpoint(p.EndpointUrl, AdapterId);

        var apiKey = ProviderSecretResolver.Resolve(p.ApiKeySecretRef, fallbackEnv: "RADIOPAD_PROVIDER_AZURE_OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new ProviderTransportException($"{AdapterId}: API key secret '{p.ApiKeySecretRef}' did not resolve.");

        var deployment = string.IsNullOrWhiteSpace(p.Model) ? "gpt-4o" : p.Model;
        var (baseUri, apiVersion) = SplitEndpoint(p.EndpointUrl);
        var url = $"{baseUri.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";

        var client = _http.CreateClient("ai");
        client.DefaultRequestHeaders.Remove("api-key");
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var body = OpenAiChatHelpers.BuildChatBody(deployment, request.SystemPrompt, request.UserPrompt, temperature: request.Temperature, outputSchema: request.OutputSchema);
        var (text, pt, ctok, ms) = await OpenAiChatHelpers.SendChatAsync(client, url, body, AdapterId, cancellationToken);

        return new AiResult(
            Text: text,
            Provider: p.Name,
            Model: deployment,
            LatencyMs: (int)ms,
            InputTokens: pt,
            OutputTokens: ctok,
            PromptVersion: request.PromptVersion);
    }

    /// <summary>
    /// Azure operators sometimes embed <c>?api-version=...</c> in the
    /// configured endpoint. Honour it when present, otherwise fall back to
    /// <see cref="DefaultApiVersion"/>.
    /// </summary>
    private static (string baseUri, string apiVersion) SplitEndpoint(string endpoint)
    {
        var qIdx = endpoint.IndexOf('?');
        if (qIdx < 0) return (endpoint, DefaultApiVersion);
        var baseUri = endpoint[..qIdx];
        var query = endpoint[(qIdx + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq];
            var val = pair[(eq + 1)..];
            if (string.Equals(key, "api-version", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                return (baseUri, Uri.UnescapeDataString(val));
        }
        return (baseUri, DefaultApiVersion);
    }
}
