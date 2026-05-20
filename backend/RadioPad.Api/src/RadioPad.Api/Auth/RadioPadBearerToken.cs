using System.Security.Cryptography;
using System.Text;

namespace RadioPad.Api.Auth;

internal static class RadioPadBearerToken
{
    public const string Prefix = "rp_";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public static bool UsesDefaultSecret =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET"))
        || string.Equals(
            Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET"),
            "dev-only-not-for-production",
            StringComparison.Ordinal);

    public static string Mint(string tenant, string email, int sessionEpoch, DateTimeOffset? issuedAt = null)
    {
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET")
            ?? "dev-only-not-for-production";
        var issued = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var raw = hmac.ComputeHash(Encoding.UTF8.GetBytes($"v2|{tenant}|{email}|{sessionEpoch}|{issued}"));
        return $"{Prefix}v2_{issued}_{Base64Url(raw)}";
    }

    public static bool IsRadioPadBearer(string token) =>
        token.StartsWith(Prefix, StringComparison.Ordinal);

    public static DateTimeOffset ExpiresAt(DateTimeOffset issuedAt) => issuedAt.Add(Lifetime);

    public static bool Matches(
        string presented,
        string tenant,
        string email,
        int sessionEpoch,
        DateTimeOffset? now = null)
    {
        if (!TryParseV2(presented, out var issuedAt))
            return false;
        var current = now ?? DateTimeOffset.UtcNow;
        if (issuedAt > current.Add(ClockSkew))
            return false;
        if (current - issuedAt > Lifetime)
            return false;

        var expected = Mint(tenant, email, sessionEpoch, issuedAt);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    private static bool TryParseV2(string token, out DateTimeOffset issuedAt)
    {
        issuedAt = default;
        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var parts = token[Prefix.Length..].Split('_', 3);
        if (parts.Length != 3 || parts[0] != "v2")
            return false;
        if (!long.TryParse(parts[1], out var issuedUnix))
            return false;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedUnix);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
