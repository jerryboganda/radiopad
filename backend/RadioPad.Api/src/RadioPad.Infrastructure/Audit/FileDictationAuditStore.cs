using System.Text;
using System.Text.Json;
using RadioPad.Application.Dictation;

namespace RadioPad.Infrastructure.Audit;

/// <summary>
/// Brief §5.7 — the on-device, append-only, encrypted dictation audit store. Each line of the
/// backing file is <c>{prevHash, hash, cipher}</c> where <c>cipher</c> is the AES-256-GCM envelope
/// of the record JSON, so PHI never touches the disk in the clear. The SHA-256 hash chain
/// (ADR-0003 design) makes any post-hoc edit detectable via <see cref="VerifyAsync"/>.
/// </summary>
public sealed class FileDictationAuditStore : IDictationAuditStore
{
    private sealed record Line(string PrevHash, string Hash, string Cipher);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _path;
    private readonly AesGcmPayloadCipher _cipher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileDictationAuditStore(string path, byte[] key)
    {
        _path = path;
        _cipher = new AesGcmPayloadCipher(key);
    }

    public async Task<DictationAuditEntry> AppendAsync(DictationAuditRecord record, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var prevHash = await LastHashAsync(ct);
            var entry = DictationAuditChain.Append(record, prevHash);
            var cipher = _cipher.Encrypt(JsonSerializer.Serialize(record, Json));
            var line = JsonSerializer.Serialize(new Line(entry.PrevHash, entry.Hash, cipher), Json);

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.AppendAllTextAsync(_path, line + "\n", Encoding.UTF8, ct);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DictationAuditEntry>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return Array.Empty<DictationAuditEntry>();

        var entries = new List<DictationAuditEntry>();
        foreach (var raw in await File.ReadAllLinesAsync(_path, ct))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var line = JsonSerializer.Deserialize<Line>(raw, Json)
                       ?? throw new InvalidDataException("Corrupt dictation-audit line.");
            var record = JsonSerializer.Deserialize<DictationAuditRecord>(_cipher.Decrypt(line.Cipher), Json)
                         ?? throw new InvalidDataException("Corrupt dictation-audit payload.");
            entries.Add(new DictationAuditEntry(record, line.PrevHash, line.Hash));
        }

        return entries;
    }

    public async Task<bool> VerifyAsync(CancellationToken ct)
        => DictationAuditChain.Verify(await ReadAllAsync(ct));

    private async Task<string> LastHashAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return DictationAuditChain.GenesisHash;

        string? lastNonEmpty = null;
        foreach (var raw in await File.ReadAllLinesAsync(_path, ct))
            if (!string.IsNullOrWhiteSpace(raw))
                lastNonEmpty = raw;

        if (lastNonEmpty is null)
            return DictationAuditChain.GenesisHash;

        var line = JsonSerializer.Deserialize<Line>(lastNonEmpty, Json);
        return line?.Hash ?? DictationAuditChain.GenesisHash;
    }
}
