using System.Collections.Concurrent;
using System.Security.Cryptography;
using RadioPad.Application.Services.Kms;

namespace RadioPad.Infrastructure.Kms;

/// <summary>
/// Iter-32 SEC-003 — in-memory cache of unwrapped tenant data-encryption-keys
/// (DEKs). The cache holds the raw 32-byte DEK for at most <see cref="TtlSeconds"/>
/// seconds (default 5 minutes / 300 s). DEKs are zeroed when evicted and are
/// never logged. Callers obtain the DEK by invoking <see cref="GetAsync"/>
/// with the wrapped envelope (b64) + the tenant's KEK reference; the cache
/// re-unwraps via the configured <see cref="IKmsResolver"/> when the entry
/// is absent or expired.
///
/// Cache key = SHA-256 hash of <c>tenantId|keyRef|wrappedDekBase64</c> so a
/// rotated DEK forces a fresh unwrap. The wrapped payload is the
/// authoritative source of truth; the cache is purely a perf cap on KMS
/// round-trips.
/// </summary>
public interface ITenantDekCache
{
    Task<byte[]> GetAsync(Guid tenantId, string keyRef, string wrappedDekBase64, CancellationToken ct);
    void Invalidate(Guid tenantId);
}

public sealed class TenantDekCache : ITenantDekCache, IDisposable
{
    /// <summary>5 minutes, per SEC-003.</summary>
    public const int TtlSeconds = 300;

    private readonly IKmsResolver _resolver;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public TenantDekCache(IKmsResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<byte[]> GetAsync(Guid tenantId, string keyRef, string wrappedDekBase64, CancellationToken ct)
    {
        var cacheKey = ComputeCacheKey(tenantId, keyRef, wrappedDekBase64);
        if (_entries.TryGetValue(cacheKey, out var entry) && !entry.IsExpired)
        {
            return entry.CopyDek();
        }

        var provider = _resolver.Resolve(keyRef);
        var wrapped = Convert.FromBase64String(wrappedDekBase64);
        var dek = await provider.UnwrapAsync(keyRef, wrapped, tenantId.ToString(), ct).ConfigureAwait(false);
        if (dek.Length != 32)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new InvalidOperationException("Unwrapped tenant DEK must be 32 bytes (AES-256).");
        }

        // Replace any stale entry; zero the previous DEK on eviction.
        if (_entries.TryRemove(cacheKey, out var stale))
        {
            stale.Zero();
        }
        _entries[cacheKey] = new Entry(dek);
        return (byte[])dek.Clone();
    }

    public void Invalidate(Guid tenantId)
    {
        var prefix = tenantId.ToString("N") + "|";
        foreach (var key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && _entries.TryRemove(key, out var e))
            {
                e.Zero();
            }
        }
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Zero();
        _entries.Clear();
    }

    private static string ComputeCacheKey(Guid tenantId, string keyRef, string wrappedDekBase64)
    {
        // Hashed so callers can never log a key that contains material from the wrapped DEK.
        var raw = $"{tenantId:N}|{keyRef}|{wrappedDekBase64}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return tenantId.ToString("N") + "|" + Convert.ToHexString(hash);
    }

    private sealed class Entry
    {
        private readonly byte[] _dek;
        private readonly long _expiresAtUnix;
        public Entry(byte[] dek)
        {
            _dek = dek;
            _expiresAtUnix = DateTimeOffset.UtcNow.AddSeconds(TtlSeconds).ToUnixTimeSeconds();
        }
        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _expiresAtUnix;
        public byte[] CopyDek() => (byte[])_dek.Clone();
        public void Zero() => CryptographicOperations.ZeroMemory(_dek);
    }
}
