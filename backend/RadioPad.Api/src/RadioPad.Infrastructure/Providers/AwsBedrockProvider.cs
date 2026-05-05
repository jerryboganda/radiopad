using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// AWS Bedrock adapter — SigV4-signed POST to
/// <c>https://bedrock-runtime.{region}.amazonaws.com/model/{modelId}/invoke</c>.
/// Default compliance class is <see cref="ProviderComplianceClass.PhiApproved"/>
/// (Bedrock is HIPAA-eligible under the AWS BAA).
///
/// <para>Configuration:
/// <list type="bullet">
///   <item><c>EndpointUrl</c> — region URL (e.g. <c>https://bedrock-runtime.us-east-1.amazonaws.com</c>) — region is parsed from the host.</item>
///   <item><c>Model</c> — full Bedrock model id (e.g. <c>anthropic.claude-3-5-sonnet-20241022-v2:0</c>).</item>
///   <item><c>ApiKeySecretRef</c> — optional <c>env:NAME</c> ref pointing at the access-key id; the secret access-key is read from
///       <c>RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY</c>; falls back to standard AWS env vars (<c>AWS_ACCESS_KEY_ID</c> / <c>AWS_SECRET_ACCESS_KEY</c> / <c>AWS_SESSION_TOKEN</c>).</item>
/// </list>
/// </para>
/// </summary>
public sealed class AwsBedrockProvider : IAiProviderAdapter
{
    public const string AdapterId = "aws-bedrock";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.PhiApproved;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<AwsBedrockProvider> _log;

    public AwsBedrockProvider(IHttpClientFactory http, ILogger<AwsBedrockProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var modelId = string.IsNullOrWhiteSpace(p.Model) ? "anthropic.claude-3-5-sonnet-20241022-v2:0" : p.Model;

        var (region, baseUri) = ResolveRegionAndBase(p.EndpointUrl);
        var url = $"{baseUri.TrimEnd('/')}/model/{Uri.EscapeDataString(modelId)}/invoke";

        var accessKeyId = ProviderSecretResolver.Resolve(p.ApiKeySecretRef, fallbackEnv: "AWS_ACCESS_KEY_ID")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY")
            ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

        if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretKey))
            throw new ProviderTransportException($"{AdapterId}: AWS credentials are not configured (set RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID / RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY).");

        var body = BuildBody(modelId, request.SystemPrompt, request.UserPrompt);
        var json = JsonSerializer.Serialize(body);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8),
        };
        httpReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        await AwsSigV4Signer.SignAsync(httpReq, accessKeyId, secretKey, region, "bedrock", sessionToken: sessionToken, cancellationToken: cancellationToken);

        var client = _http.CreateClient("ai");
        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await client.SendAsync(httpReq, cancellationToken);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            throw new ProviderTransportException($"{AdapterId}: HTTP transport failure: {hre.Message}", inner: hre);
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

            var (text, pt, ctok) = ParseResponse(doc.RootElement, modelId);
            return new AiResult(
                Text: text,
                Provider: p.Name,
                Model: modelId,
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
    /// Bedrock body shape varies by model family. We support the two most
    /// common: Anthropic Claude (Messages API) and Amazon Titan (text input).
    /// Falls back to Claude shape because Anthropic is the default model id.
    /// </summary>
    private static object BuildBody(string modelId, string systemPrompt, string userPrompt)
    {
        if (modelId.StartsWith("amazon.titan-", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                inputText = $"{systemPrompt}\n\n{userPrompt}",
                textGenerationConfig = new { maxTokenCount = 1024, temperature = 0.2, topP = 0.9 },
            };
        }
        // Anthropic / default
        return new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 1024,
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = userPrompt },
            },
        };
    }

    private static (string text, int promptTokens, int completionTokens) ParseResponse(JsonElement root, string modelId)
    {
        // Anthropic Messages-on-Bedrock: { content: [{ text }], usage: { input_tokens, output_tokens } }
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
        {
            var first = content[0];
            var text = first.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            var pt = 0; var ctok = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var i) && i.ValueKind == JsonValueKind.Number) pt = i.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number) ctok = o.GetInt32();
            }
            return (text, pt, ctok);
        }

        // Titan: { results: [{ outputText, tokenCount }], inputTextTokenCount }
        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            var first = results[0];
            var text = first.TryGetProperty("outputText", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            var pt = root.TryGetProperty("inputTextTokenCount", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
            var ctok = first.TryGetProperty("tokenCount", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0;
            return (text, pt, ctok);
        }

        return ("", 0, 0);
    }

    private static (string region, string baseUri) ResolveRegionAndBase(string endpointUrl)
    {
        var raw = string.IsNullOrWhiteSpace(endpointUrl)
            ? "https://bedrock-runtime.us-east-1.amazonaws.com"
            : endpointUrl;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new ProviderTransportException($"{AdapterId}: malformed endpoint URL '{endpointUrl}'.");

        // Host pattern: bedrock-runtime.{region}.amazonaws.com (or bedrock.{region}...)
        var host = uri.Host;
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var region = "us-east-1";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith("bedrock", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < parts.Length)
                {
                    region = parts[i + 1];
                    break;
                }
            }
        }
        return (region, $"{uri.Scheme}://{uri.Host}");
    }
}
