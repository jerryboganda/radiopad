using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Dictation;
using RadioPad.Infrastructure.Audit;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §5.7 — the dictation audit trail: append-only, hash-chained (ADR-0003 design), encrypted
/// at rest, persisting raw transcript + final report + diff + template + model versions locally.
/// </summary>
public class DictationAuditTests
{
    private static DictationAuditRecord Record(string reportId, string raw, string ts) =>
        new(
            ReportId: reportId,
            RawTranscript: raw,
            CorrectedTranscript: raw,
            FinalSections: new Dictionary<string, string> { ["findings"] = raw },
            Diff: "n/a",
            TemplateId: "chest_ct_v1",
            SttModel: "medasr-1.0.0",
            FormatterProvider: "local-medgemma",
            FormatterModel: "medgemma-1.5-4b-q4",
            Accepted: true,
            TimestampUtc: ts);

    // ── hash chain ───────────────────────────────────────────────────────

    [Fact]
    public void Append_Links_To_Prev_And_Hash_Is_Deterministic()
    {
        var rec = Record("r1", "3.2 cm nodule", "2026-07-18T00:00:00Z");

        var e1 = DictationAuditChain.Append(rec, DictationAuditChain.GenesisHash);
        var e2 = DictationAuditChain.Append(rec, DictationAuditChain.GenesisHash);

        Assert.Equal(DictationAuditChain.GenesisHash, e1.PrevHash);
        Assert.Equal(e1.Hash, e2.Hash);                    // deterministic
        Assert.Equal(64, e1.Hash.Length);                  // SHA-256 hex
    }

    [Fact]
    public void Verify_Accepts_Intact_Chain_And_Rejects_Tampering()
    {
        var e1 = DictationAuditChain.Append(Record("r1", "a", "t1"), DictationAuditChain.GenesisHash);
        var e2 = DictationAuditChain.Append(Record("r2", "b", "t2"), e1.Hash);

        Assert.True(DictationAuditChain.Verify(new[] { e1, e2 }));

        var tampered = e2 with { Record = e2.Record with { RawTranscript = "MUTATED" } };
        Assert.False(DictationAuditChain.Verify(new[] { e1, tampered }));
    }

    // ── AES-256-GCM at-rest cipher ───────────────────────────────────────

    [Fact]
    public void Cipher_RoundTrips_And_Uses_Fresh_Nonce()
    {
        var cipher = new AesGcmPayloadCipher(RandomNumberGenerator.GetBytes(32));

        var a = cipher.Encrypt("raw transcript with PHI");
        var b = cipher.Encrypt("raw transcript with PHI");

        Assert.NotEqual(a, b);                              // fresh nonce each time
        Assert.Equal("raw transcript with PHI", cipher.Decrypt(a));
        Assert.Equal("raw transcript with PHI", cipher.Decrypt(b));
    }

    [Fact]
    public void Cipher_Rejects_Tampered_Ciphertext()
    {
        var cipher = new AesGcmPayloadCipher(RandomNumberGenerator.GetBytes(32));
        var envelope = cipher.Encrypt("secret");
        var raw = Convert.FromBase64String(envelope);
        raw[^1] ^= 0xFF;                                    // flip a ciphertext byte
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(Convert.ToBase64String(raw)));
    }

    // ── file-backed encrypted append-only store ──────────────────────────

    [Fact]
    public async Task FileStore_Appends_Encrypted_Chained_Records_And_Verifies()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rp-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var store = new FileDictationAuditStore(Path.Combine(dir, "dictation-audit.jsonl"), key);

            await store.AppendAsync(Record("r1", "3.2 cm nodule in the right lobe", "t1"), CancellationToken.None);
            await store.AppendAsync(Record("r2", "no effusion", "t2"), CancellationToken.None);

            var all = await store.ReadAllAsync(CancellationToken.None);
            Assert.Equal(2, all.Count);
            Assert.Equal("3.2 cm nodule in the right lobe", all[0].Record.RawTranscript); // decrypts
            Assert.Equal(all[0].Hash, all[1].PrevHash);                                    // chained
            Assert.True(await store.VerifyAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FileStore_Persists_Ciphertext_Not_Plaintext()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rp-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "dictation-audit.jsonl");
            var store = new FileDictationAuditStore(path, RandomNumberGenerator.GetBytes(32));
            await store.AppendAsync(Record("r1", "PATIENT_NAME_PHI_MARKER", "t1"), CancellationToken.None);

            var onDisk = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("PATIENT_NAME_PHI_MARKER", onDisk);   // encrypted at rest
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
