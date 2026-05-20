using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RadioPad.Api.Auth;

public static class RadioPadBearerTokens
{
    public const string Prefix = "rp_";
    private const string DefaultDevSecret = "dev-only-not-for-production";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    public static DateTimeOffset ExpiresAt(DateTimeOffset issuedAt) => issuedAt.Add(DefaultLifetime);

    public static void ValidateStartupSecret(IHostEnvironment env)
    {
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET");
        if (env.IsProduction() && !IsStrongSecret(secret))
            throw new InvalidOperationException("RADIOPAD_AUTH_SECRET must be set to a non-default value of at least 32 characters in Production.");
    }

    public static string Mint(
        string tenant,
        string email,
        int sessionEpoch,
        IHostEnvironment? env = null,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null)
    {
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(lifetime ?? DefaultLifetime);
        var payload = new TokenPayload(
            v: 2,
            t: tenant,
            u: email,
            e: sessionEpoch,
            iat: issuedAt.ToUnixTimeSeconds(),
            exp: expiresAt.ToUnixTimeSeconds(),
            jti: Guid.NewGuid().ToString("N"));
        var payloadSegment = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Sign(payloadSegment, ResolveSecret(env));
        return Prefix + payloadSegment + "." + signature;
    }

    public static bool TryValidate(
        string token,
        string tenant,
        string email,
        int sessionEpoch,
        IHostEnvironment? env,
        out string reason,
        DateTimeOffset? now = null)
    {
        reason = "invalid_token";
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var body = token[Prefix.Length..];
        var parts = body.Split('.', 2);
        if (parts.Length != 2) return false;

        string expectedSignature;
        try
        {
            expectedSignature = Sign(parts[0], ResolveSecret(env));
        }
        catch (InvalidOperationException)
        {
            reason = "auth_secret_not_configured";
            return false;
        }

        if (!FixedTimeEquals(parts[1], expectedSignature)) return false;

        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(Base64UrlDecode(parts[0]));
        }
        catch
        {
            return false;
        }

        if (payload is null || payload.v != 2) return false;
        if (!string.Equals(payload.t, tenant, StringComparison.Ordinal) ||
            !string.Equals(payload.u, email, StringComparison.OrdinalIgnoreCase) ||
            payload.e != sessionEpoch)
        {
            return false;
        }

        var clock = now ?? DateTimeOffset.UtcNow;
        if (payload.exp <= clock.ToUnixTimeSeconds())
        {
            reason = "expired_token";
            return false;
        }

        if (payload.iat > clock.AddMinutes(5).ToUnixTimeSeconds())
        {
            reason = "invalid_token";
            return false;
        }

        reason = "ok";
        return true;
    }

    public static bool TryReadUnvalidatedContext(string token, out string tenant, out string email)
    {
        tenant = string.Empty;
        email = string.Empty;
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var body = token[Prefix.Length..];
        var parts = body.Split('.', 2);
        if (parts.Length != 2) return false;

        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(Base64UrlDecode(parts[0]));
        }
        catch
        {
            return false;
        }

        if (payload is null || payload.v != 2) return false;
        if (string.IsNullOrWhiteSpace(payload.t) || string.IsNullOrWhiteSpace(payload.u)) return false;
        tenant = payload.t;
        email = payload.u;
        return true;
    }

    private static string ResolveSecret(IHostEnvironment? env)
    {
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET");
        if (IsStrongSecret(secret)) return secret!;
        if (env?.IsProduction() == true)
            throw new InvalidOperationException("RADIOPAD_AUTH_SECRET must be configured in Production.");
        return DefaultDevSecret;
    }

    private static bool IsStrongSecret(string? secret) =>
        !string.IsNullOrWhiteSpace(secret) &&
        secret.Length >= 32 &&
        !string.Equals(secret, DefaultDevSecret, StringComparison.Ordinal);

    private static string Sign(string payloadSegment, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes("v2." + payloadSegment)));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record TokenPayload(int v, string t, string u, int e, long iat, long exp, string jti);
}