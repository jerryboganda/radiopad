using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RadioPad.Application.Dictation;

/// <summary>
/// Brief §5.7 — the complete, medico-legally reviewable record of one dictation→report run. Stored
/// encrypted, on-device, append-only. Every AI-applied change is reconstructable from the raw
/// transcript + final report + diff.
/// </summary>
public sealed record DictationAuditRecord(
    string ReportId,
    string RawTranscript,
    string CorrectedTranscript,
    IReadOnlyDictionary<string, string> FinalSections,
    string Diff,
    string? TemplateId,
    string SttModel,
    string FormatterProvider,
    string FormatterModel,
    bool Accepted,
    string TimestampUtc);

/// <summary>One entry in the append-only hash chain (ADR-0003 design): a record plus its links.</summary>
public sealed record DictationAuditEntry(DictationAuditRecord Record, string PrevHash, string Hash);

/// <summary>Persistence contract for the local dictation audit trail (§5.7).</summary>
public interface IDictationAuditStore
{
    Task<DictationAuditEntry> AppendAsync(DictationAuditRecord record, CancellationToken ct);
    Task<IReadOnlyList<DictationAuditEntry>> ReadAllAsync(CancellationToken ct);
    Task<bool> VerifyAsync(CancellationToken ct);
}

/// <summary>
/// Brief §5.7 — SHA-256 hash chain over dictation audit records (reuses the ADR-0003 chain design).
/// Each entry's hash covers the record plus the previous hash, so any post-hoc mutation breaks the
/// chain and <see cref="Verify"/> fails.
/// </summary>
public static class DictationAuditChain
{
    /// <summary>The zero hash that anchors the first entry.</summary>
    public static readonly string GenesisHash = new('0', 64);

    private static readonly JsonSerializerOptions CanonicalJson = new() { WriteIndented = false };

    public static string ComputeHash(DictationAuditRecord record, string prevHash)
    {
        var json = JsonSerializer.Serialize(record, CanonicalJson);
        var bytes = Encoding.UTF8.GetBytes(prevHash + "\n" + json);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static DictationAuditEntry Append(DictationAuditRecord record, string prevHash)
        => new(record, prevHash, ComputeHash(record, prevHash));

    /// <summary>Recomputes the whole chain and confirms every link and hash is intact.</summary>
    public static bool Verify(IReadOnlyList<DictationAuditEntry> entries)
    {
        var prev = GenesisHash;
        foreach (var e in entries)
        {
            if (e.PrevHash != prev)
                return false;
            if (ComputeHash(e.Record, e.PrevHash) != e.Hash)
                return false;
            prev = e.Hash;
        }

        return true;
    }
}

/// <summary>
/// Brief §5.7 — authenticated AES-256-GCM encryption for the audit payload at rest. A fresh 96-bit
/// nonce is generated per call; the envelope is base64(<c>nonce | tag | ciphertext</c>). Decryption
/// throws <see cref="CryptographicException"/> if the ciphertext or tag was tampered with.
/// </summary>
public sealed class AesGcmPayloadCipher
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private readonly byte[] _key;

    public AesGcmPayloadCipher(byte[] key)
    {
        if (key is not { Length: 32 })
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, pt, ct, tag);

        var envelope = new byte[NonceBytes + TagBytes + ct.Length];
        Buffer.BlockCopy(nonce, 0, envelope, 0, NonceBytes);
        Buffer.BlockCopy(tag, 0, envelope, NonceBytes, TagBytes);
        Buffer.BlockCopy(ct, 0, envelope, NonceBytes + TagBytes, ct.Length);
        return Convert.ToBase64String(envelope);
    }

    public string Decrypt(string envelope)
    {
        var buf = Convert.FromBase64String(envelope);
        if (buf.Length < NonceBytes + TagBytes)
            throw new CryptographicException("Audit envelope is too short to be valid.");

        var nonce = buf.AsSpan(0, NonceBytes);
        var tag = buf.AsSpan(NonceBytes, TagBytes);
        var ct = buf.AsSpan(NonceBytes + TagBytes);
        var pt = new byte[ct.Length];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}
