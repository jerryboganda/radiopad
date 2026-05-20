using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Google Cloud Vertex AI Gemini adapter
/// (<c>generative-ai</c> models served via <c>{location}-aiplatform.googleapis.com</c>).
/// Default compliance class is <see cref="ProviderComplianceClass.PhiApproved"/>
/// (Google Workspace BAA covers Vertex AI per Google Cloud HIPAA-included services).
///
/// <para>Configuration:
/// <list type="bullet">
///   <item><c>EndpointUrl</c> — full Vertex URL or just the regional host (e.g. <c>https://us-central1-aiplatform.googleapis.com</c>); when empty defaults to <c>us-central1</c>.</item>
///   <item><c>Model</c> — Gemini model id (e.g. <c>gemini-1.5-pro</c>).</item>
///   <item><c>ApiKeySecretRef</c> — <c>env:RADIOPAD_PROVIDER_GCP_SERVICE_ACCOUNT_JSON</c> pointing at a service-account JSON blob; empty falls back to <c>GOOGLE_APPLICATION_CREDENTIALS_JSON</c> or a pre-acquired bearer token in <c>RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN</c>.</item>
///   <item>GCP project id is read from the JSON's <c>project_id</c> field, or from <c>RADIOPAD_PROVIDER_GCP_PROJECT_ID</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GoogleVertexAiProvider : IAiProviderAdapter
{
    public const string AdapterId = "google-vertex";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.PhiApproved;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<GoogleVertexAiProvider> _log;

    public GoogleVertexAiProvider(IHttpClientFactory http, ILogger<GoogleVertexAiProvider> log)
    {
        _http = http;
        _log = log;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var model = string.IsNullOrWhiteSpace(p.Model) ? "gemini-1.5-pro" : p.Model;
        var (location, baseUri) = ResolveLocation(p.EndpointUrl);

        var (token, projectId) = await AcquireAccessTokenAsync(p, cancellationToken);
        if (string.IsNullOrEmpty(token))
            throw new ProviderTransportException($"{AdapterId}: could not acquire Google access token.");
        if (string.IsNullOrEmpty(projectId))
            throw new ProviderTransportException($"{AdapterId}: GCP project id is not configured (set RADIOPAD_PROVIDER_GCP_PROJECT_ID or include project_id in the service-account JSON).");

        var url = $"{baseUri.TrimEnd('/')}/v1/projects/{Uri.EscapeDataString(projectId)}/locations/{Uri.EscapeDataString(location)}/publishers/google/models/{Uri.EscapeDataString(model)}:generateContent";

        var body = new
        {
            contents = new object[]
            {
                new { role = "user", parts = new object[] { new { text = request.UserPrompt } } },
            },
            systemInstruction = new { parts = new object[] { new { text = request.SystemPrompt } } },
            generationConfig = new { temperature = 0.2, maxOutputTokens = 1024 },
        };

        var client = _http.CreateClient("ai");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

            var (text, pt, ctok) = ParseResponse(doc.RootElement);
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

    private static (string text, int promptTokens, int completionTokens) ParseResponse(JsonElement root)
    {
        var text = "";
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                text = t.GetString() ?? "";
            }
        }
        var pt = 0; var ctok = 0;
        if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("promptTokenCount", out var p) && p.ValueKind == JsonValueKind.Number) pt = p.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var c) && c.ValueKind == JsonValueKind.Number) ctok = c.GetInt32();
        }
        return (text, pt, ctok);
    }

    private static (string location, string baseUri) ResolveLocation(string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
            return ("us-central1", "https://us-central1-aiplatform.googleapis.com");

        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
            return ("us-central1", "https://us-central1-aiplatform.googleapis.com");
        OpenAiChatHelpers.ValidateHostedEndpoint(endpointUrl, AdapterId);

        var host = uri.Host;
        var location = "us-central1";
        var dashIdx = host.IndexOf("-aiplatform.", StringComparison.OrdinalIgnoreCase);
        if (dashIdx > 0) location = host[..dashIdx];
        return (location, $"{uri.Scheme}://{uri.Host}");
    }

    /// <summary>
    /// Acquires a short-lived OAuth 2.0 access token from Google's token
    /// endpoint using a service-account JWT (RS256). Falls back to a
    /// pre-acquired token in <c>RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN</c> for
    /// dev/CI.
    /// </summary>
    private async Task<(string? token, string? projectId)> AcquireAccessTokenAsync(
        Domain.Entities.ProviderConfig p, CancellationToken ct)
    {
        var preAcquired = Environment.GetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN");
        if (!string.IsNullOrEmpty(preAcquired))
        {
            return (preAcquired, Environment.GetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID"));
        }

        var saJson = ProviderSecretResolver.Resolve(p.ApiKeySecretRef, fallbackEnv: "RADIOPAD_PROVIDER_GCP_SERVICE_ACCOUNT_JSON")
            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS_JSON");
        if (string.IsNullOrEmpty(saJson))
            return (null, null);

        try
        {
            using var saDoc = JsonDocument.Parse(saJson);
            var clientEmail = saDoc.RootElement.GetProperty("client_email").GetString() ?? "";
            var privateKeyPem = saDoc.RootElement.GetProperty("private_key").GetString() ?? "";
            var projectId = saDoc.RootElement.TryGetProperty("project_id", out var pid) ? pid.GetString() : null;
            projectId ??= Environment.GetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID");

            var jwt = BuildServiceAccountJwt(clientEmail, privateKeyPem);
            var client = _http.CreateClient("ai");
            using var resp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt),
            }), ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await OpenAiChatHelpers.SafeReadAsync(resp, ct);
                throw new ProviderTransportException(
                    $"{AdapterId}: token endpoint returned HTTP {(int)resp.StatusCode}.",
                    statusCode: (int)resp.StatusCode,
                    responseBody: body);
            }
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            using var tok = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            var access = tok.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            return (access, projectId);
        }
        catch (ProviderTransportException) { throw; }
        catch (Exception ex)
        {
            throw new ProviderTransportException($"{AdapterId}: failed to mint Google access token: {ex.Message}", inner: ex);
        }
    }

    private static string BuildServiceAccountJwt(string clientEmail, string privateKeyPem)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new { alg = "RS256", typ = "JWT" };
        var claims = new
        {
            iss = clientEmail,
            scope = "https://www.googleapis.com/auth/cloud-platform",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600,
        };
        var headerSeg = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var claimSeg = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signingInput = $"{headerSeg}.{claimSeg}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var sig = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(sig)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
