using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RadioPad.Application.Services.Push;

/// <summary>
/// PRD MOB-007 — abstract push sender. Implementations dispatch to APNs or
/// FCM. Payloads are intentionally PHI-free: title is "RadioPad", body is a
/// generic notification, and the deep-link is encoded in <c>data.kind</c> +
/// <c>data.entityId</c> so the client can route on launch.
/// </summary>
public interface IPushSender
{
    string Platform { get; }
    /// <summary>Returns true when the runtime config (env vars) is complete.</summary>
    bool IsConfigured { get; }
    Task SendAsync(string deviceToken, PushPayload payload, CancellationToken ct);
}

public sealed record PushPayload(string Title, string Body, string Kind, string EntityId);

public sealed class PushNotConfiguredException : Exception
{
    public PushNotConfiguredException(string message) : base(message) { }
}

internal static class PushEnv
{
    public const string ApnsKeyP8 = "RADIOPAD_APNS_KEY_P8";
    public const string ApnsKeyId = "RADIOPAD_APNS_KEY_ID";
    public const string ApnsTeamId = "RADIOPAD_APNS_TEAM_ID";
    public const string ApnsBundleId = "RADIOPAD_APNS_BUNDLE_ID";
    public const string FcmProjectId = "RADIOPAD_FCM_PROJECT_ID";
    public const string FcmServiceAccountJson = "RADIOPAD_FCM_SERVICE_ACCOUNT_JSON";
}

/// <summary>
/// APNs sender using token-based authentication (.p8 ES256 JWT).
/// HTTP/2 endpoint: https://api.push.apple.com/3/device/{deviceToken}.
/// </summary>
public sealed class ApnsSender : IPushSender
{
    private readonly IHttpClientFactory _httpFactory;

    public ApnsSender(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public string Platform => "ios";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.ApnsKeyP8)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.ApnsKeyId)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.ApnsTeamId)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.ApnsBundleId));

    public async Task SendAsync(string deviceToken, PushPayload payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new PushNotConfiguredException("APNs (.p8) credentials are not configured.");

        var keyPath = Environment.GetEnvironmentVariable(PushEnv.ApnsKeyP8)!;
        var keyId = Environment.GetEnvironmentVariable(PushEnv.ApnsKeyId)!;
        var teamId = Environment.GetEnvironmentVariable(PushEnv.ApnsTeamId)!;
        var bundleId = Environment.GetEnvironmentVariable(PushEnv.ApnsBundleId)!;

        if (!File.Exists(keyPath))
            throw new PushNotConfiguredException("APNs .p8 key file not found at configured path.");

        var jwt = BuildAppleJwt(keyPath, keyId, teamId);

        var apsBody = new JsonObject
        {
            ["aps"] = new JsonObject
            {
                ["alert"] = new JsonObject
                {
                    ["title"] = payload.Title,
                    ["body"] = payload.Body,
                },
                ["sound"] = "default",
            },
            ["kind"] = payload.Kind,
            ["entityId"] = payload.EntityId,
        };

        var http = _httpFactory.CreateClient("apns");
        http.DefaultRequestVersion = System.Net.HttpVersion.Version20;
        http.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrHigher;
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.push.apple.com/3/device/{Uri.EscapeDataString(deviceToken)}")
        {
            Version = System.Net.HttpVersion.Version20,
            Content = new StringContent(apsBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
        req.Headers.TryAddWithoutValidation("apns-topic", bundleId);
        req.Headers.TryAddWithoutValidation("apns-push-type", "alert");

        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            // Body may carry an APNs reason; never log secrets/tokens.
            var reason = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"APNs send failed: {(int)res.StatusCode} {reason}");
        }
    }

    private static string BuildAppleJwt(string keyPath, string keyId, string teamId)
    {
        var pem = File.ReadAllText(keyPath);
        using var ec = ECDsa.Create();
        ec.ImportFromPem(pem);

        var header = new JsonObject { ["alg"] = "ES256", ["kid"] = keyId, ["typ"] = "JWT" }.ToJsonString();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new JsonObject { ["iss"] = teamId, ["iat"] = iat }.ToJsonString();

        var encHeader = Base64Url(Encoding.UTF8.GetBytes(header));
        var encPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{encHeader}.{encPayload}";
        var sig = ec.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"{signingInput}.{Base64Url(sig)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// FCM HTTP v1 sender. Mints a short-lived OAuth2 access token from the
/// Google service-account JSON (RS256 JWT bearer flow) and POSTs to
/// https://fcm.googleapis.com/v1/projects/{projectId}/messages:send.
/// </summary>
public sealed class FcmSender : IPushSender
{
    private readonly IHttpClientFactory _httpFactory;
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiresAt = DateTimeOffset.MinValue;

    public FcmSender(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public string Platform => "android";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.FcmProjectId)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PushEnv.FcmServiceAccountJson));

    public async Task SendAsync(string deviceToken, PushPayload payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new PushNotConfiguredException("FCM service-account credentials are not configured.");

        var projectId = Environment.GetEnvironmentVariable(PushEnv.FcmProjectId)!;
        var saPath = Environment.GetEnvironmentVariable(PushEnv.FcmServiceAccountJson)!;
        if (!File.Exists(saPath))
            throw new PushNotConfiguredException("FCM service-account JSON file not found at configured path.");

        var accessToken = await GetAccessTokenAsync(saPath, ct);

        var body = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["token"] = deviceToken,
                ["notification"] = new JsonObject
                {
                    ["title"] = payload.Title,
                    ["body"] = payload.Body,
                },
                ["data"] = new JsonObject
                {
                    ["kind"] = payload.Kind,
                    ["entityId"] = payload.EntityId,
                },
            },
        };

        var http = _httpFactory.CreateClient("fcm");
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var reason = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"FCM send failed: {(int)res.StatusCode} {reason}");
        }
    }

    private async Task<string> GetAccessTokenAsync(string serviceAccountJsonPath, CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedExpiresAt.AddMinutes(-2))
            return _cachedAccessToken;

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(serviceAccountJsonPath, ct));
        var root = doc.RootElement;
        var clientEmail = root.GetProperty("client_email").GetString()!;
        var privateKey = root.GetProperty("private_key").GetString()!;
        var tokenUri = root.TryGetProperty("token_uri", out var tu)
            ? (tu.GetString() ?? "https://oauth2.googleapis.com/token")
            : "https://oauth2.googleapis.com/token";

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT" }.ToJsonString();
        var claim = new JsonObject
        {
            ["iss"] = clientEmail,
            ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
            ["aud"] = tokenUri,
            ["iat"] = now,
            ["exp"] = now + 3600,
        }.ToJsonString();

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        var encHeader = Base64Url(Encoding.UTF8.GetBytes(header));
        var encClaim = Base64Url(Encoding.UTF8.GetBytes(claim));
        var signingInput = $"{encHeader}.{encClaim}";
        var sig = rsa.SignData(Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var jwt = $"{signingInput}.{Base64Url(sig)}";

        var http = _httpFactory.CreateClient("fcm-oauth");
        using var res = await http.PostAsync(tokenUri, new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string,string>("assertion", jwt),
        }), ct);
        res.EnsureSuccessStatusCode();
        var tok = await res.Content.ReadFromJsonAsync<OauthTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty OAuth token response from Google.");

        _cachedAccessToken = tok.access_token;
        _cachedExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tok.expires_in);
        return _cachedAccessToken!;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

#pragma warning disable IDE1006 // OAuth2 wire-format names
    private sealed record OauthTokenResponse(string access_token, int expires_in, string token_type);
#pragma warning restore IDE1006
}

/// <summary>Resolves the right <see cref="IPushSender"/> for a platform string.</summary>
public sealed class PushSenderRegistry
{
    private readonly Dictionary<string, IPushSender> _byPlatform;

    public PushSenderRegistry(IEnumerable<IPushSender> senders)
    {
        _byPlatform = senders.ToDictionary(s => s.Platform, StringComparer.OrdinalIgnoreCase);
    }

    public IPushSender? Resolve(string platform) =>
        _byPlatform.TryGetValue(platform, out var s) ? s : null;
}
