using System.Security.Cryptography;

namespace RadioPad.Api.Auth;

/// <summary>
/// PRD AUTH-001 (password tier) — PBKDF2/SHA-256 password hashing. Hand-rolled
/// to match the repo's dependency-free crypto style (see the hand-rolled TOTP /
/// HMAC bearer code) rather than pulling in ASP.NET Identity. The stored value
/// is self-describing so the work factor can be raised later without a data
/// migration:
///
/// <code>pbkdf2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</code>
///
/// Verification is constant-time via <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// <see cref="User.PasswordHash"/> stores a one-way digest, so it is intentionally
/// NOT routed through the at-rest column encryptor (equality lookups are never
/// performed on it and a hash is not a secret in the recoverable sense).
/// </summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2";
    private const int DefaultIterations = 120_000; // OWASP PBKDF2-SHA256 floor (2024+)
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private static readonly HashAlgorithmName Prf = HashAlgorithmName.SHA256;

    /// <summary>Minimum password length enforced by the API surface.</summary>
    public const int MinLength = 12;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, Prf, HashBytes);
        return $"{Scheme}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (expected.Length == 0) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Prf, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Generates a human-handoff temporary password (admin-issued). Avoids
    /// ambiguous characters and always satisfies the API length floor.
    /// </summary>
    public static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        Span<char> buffer = stackalloc char[16];
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(buffer);
    }
}
