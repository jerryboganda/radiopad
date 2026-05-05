namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-31 SEC-002 — column-level at-rest encryption for sensitive byte
/// arrays (TOTP secrets, ingest bearer tokens, magic-link hashes, etc.).
/// Implementations envelope-encrypt with a per-tenant or platform data key
/// resolved from <see cref="Services.Kms.IKmsProvider"/>; the wrapped data
/// key is never logged. Encrypted blobs include their AES-GCM nonce + tag
/// so they can be decrypted without out-of-band metadata.
/// </summary>
public interface IColumnEncryptor
{
    /// <summary>Encrypt arbitrary bytes. Empty input returns empty output.</summary>
    byte[] Encrypt(byte[] plaintext);

    /// <summary>Decrypt bytes previously produced by <see cref="Encrypt"/>.</summary>
    byte[] Decrypt(byte[] ciphertext);

    /// <summary>
    /// Helper for storing encrypted bytes in <c>string</c>-typed EF Core
    /// columns. Empty / null input → empty string. Output is base64.
    /// </summary>
    string EncryptString(string? plaintext);

    /// <summary>
    /// Decrypt a base64 string produced by <see cref="EncryptString"/>. Empty
    /// input returns empty string. Strings that don't carry the version
    /// prefix (legacy unencrypted data) are returned verbatim so the
    /// migration to encrypted-at-rest is forward-compatible.
    /// </summary>
    string DecryptString(string? ciphertext);
}
