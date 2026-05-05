using System.Security.Cryptography;
using System.Text;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Kms;

namespace RadioPad.Infrastructure.Security;

/// <summary>
/// Iter-31 SEC-002 — AES-256-GCM column encryptor. The data key is resolved
/// from a configured <see cref="IKmsProvider"/> reference (<c>RADIOPAD_COLUMN_KEY_REF</c>,
/// e.g. <c>"env:RADIOPAD_COLUMN_KEY_WRAPPED"</c>) and unwrapped once at
/// startup. When no reference is configured (dev/test), a deterministic
/// fallback key derived from <c>"radiopad-dev-column-key/v1"</c> is used so
/// integration tests run without operator setup.
///
/// Output blob: <c>[1 byte version=1][12 byte nonce][ciphertext][16 byte tag]</c>.
/// Strings: base64 of that blob, prefixed with <c>"enc:v1:"</c> so callers can
/// distinguish legacy plaintext from ciphertext during a forward migration.
/// </summary>
public sealed class AesGcmColumnEncryptor : IColumnEncryptor
{
    private const byte Version = 1;
    private const string StringPrefix = "enc:v1:";
    private readonly byte[] _dataKey;

    public AesGcmColumnEncryptor(byte[] dataKey)
    {
        if (dataKey is null || dataKey.Length != 32)
            throw new ArgumentException("AesGcmColumnEncryptor requires a 32-byte data key.", nameof(dataKey));
        _dataKey = dataKey;
    }

    /// <summary>
    /// Build the encryptor by resolving <paramref name="keyRef"/> through the
    /// supplied resolver. <paramref name="wrappedDek"/> is the previously
    /// wrapped data key (b64). When <paramref name="keyRef"/> is null/empty
    /// the encryptor uses a deterministic dev fallback (NOT FOR PRODUCTION).
    /// </summary>
    public static async Task<AesGcmColumnEncryptor> CreateAsync(
        IKmsResolver? resolver,
        string? keyRef,
        string? wrappedDekBase64,
        CancellationToken ct)
    {
        if (resolver is null || string.IsNullOrWhiteSpace(keyRef) || string.IsNullOrWhiteSpace(wrappedDekBase64))
        {
            return new AesGcmColumnEncryptor(DevFallbackKey());
        }
        var provider = resolver.Resolve(keyRef);
        var wrapped = Convert.FromBase64String(wrappedDekBase64);
        var dek = await provider.UnwrapAsync(keyRef, wrapped, ct);
        if (dek.Length != 32)
            throw new InvalidOperationException("Unwrapped column data key must be 32 bytes.");
        return new AesGcmColumnEncryptor(dek);
    }

    private static byte[] DevFallbackKey()
    {
        // Deterministic so tests are reproducible. NOT secure for prod;
        // production MUST set RADIOPAD_COLUMN_KEY_REF + the wrapped DEK.
        return SHA256.HashData(Encoding.UTF8.GetBytes("radiopad-dev-column-key/v1"));
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        if (plaintext is null || plaintext.Length == 0) return Array.Empty<byte>();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_dataKey, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        var blob = new byte[1 + 12 + cipher.Length + 16];
        blob[0] = Version;
        Buffer.BlockCopy(nonce, 0, blob, 1, 12);
        Buffer.BlockCopy(cipher, 0, blob, 13, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, 13 + cipher.Length, 16);
        return blob;
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0) return Array.Empty<byte>();
        if (ciphertext.Length < 1 + 12 + 16 || ciphertext[0] != Version)
            throw new CryptographicException("Invalid column ciphertext envelope.");
        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[ciphertext.Length - 1 - 12 - 16];
        Buffer.BlockCopy(ciphertext, 1, nonce, 0, 12);
        Buffer.BlockCopy(ciphertext, 13, cipher, 0, cipher.Length);
        Buffer.BlockCopy(ciphertext, 13 + cipher.Length, tag, 0, 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_dataKey, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public string EncryptString(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var blob = Encrypt(Encoding.UTF8.GetBytes(plaintext));
        return StringPrefix + Convert.ToBase64String(blob);
    }

    public string DecryptString(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        if (!ciphertext.StartsWith(StringPrefix, StringComparison.Ordinal))
        {
            // Legacy plaintext rows during forward-migration. Pass through.
            return ciphertext;
        }
        var blob = Convert.FromBase64String(ciphertext[StringPrefix.Length..]);
        var plain = Decrypt(blob);
        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>
/// Static accessor used by EF Core <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>s
/// in <c>RadioPadDbContext.OnModelCreating</c>. The application sets
/// <see cref="Current"/> at startup so the model-build step picks up the
/// configured encryptor without dragging DI into the model layer.
/// </summary>
public static class ColumnEncryptorAccessor
{
    private static IColumnEncryptor? _current;

    public static IColumnEncryptor Current
    {
        get => _current ??= new AesGcmColumnEncryptor(SHA256.HashData(Encoding.UTF8.GetBytes("radiopad-dev-column-key/v1")));
        set => _current = value;
    }
}
