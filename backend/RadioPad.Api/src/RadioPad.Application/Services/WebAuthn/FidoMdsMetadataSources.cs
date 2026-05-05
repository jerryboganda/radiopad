using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace RadioPad.Application.Services.WebAuthn;

/// <summary>
/// Iter-35 — embedded FIDO MDS3 root CA source. Reads
/// <c>fido-mds3-roots.json</c> co-located with this assembly (a list of
/// base64-DER-encoded root certificates extracted from the FIDO Alliance
/// MDS3 BLOB at build time). The file is resolved next to the application
/// DLL so operators can update it out-of-band without rebuilding the
/// service.
/// </summary>
public sealed class EmbeddedFidoMdsMetadataSource : IFidoMdsMetadataSource
{
    private readonly Lazy<IReadOnlyList<X509Certificate2>> _roots;

    public EmbeddedFidoMdsMetadataSource(string? overridePath = null)
    {
        _roots = new Lazy<IReadOnlyList<X509Certificate2>>(() => Load(overridePath));
    }

    public IReadOnlyList<X509Certificate2> GetTrustedRoots() => _roots.Value;

    private static IReadOnlyList<X509Certificate2> Load(string? overridePath)
    {
        var path = overridePath ?? DefaultPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return Array.Empty<X509Certificate2>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<X509Certificate2>();
            var list = new List<X509Certificate2>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var b64 = entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()
                    : entry.TryGetProperty("certificate", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(b64)) continue;
                try { list.Add(new X509Certificate2(Convert.FromBase64String(b64.Trim()))); }
                catch { /* skip malformed entry */ }
            }
            return list;
        }
        catch
        {
            return Array.Empty<X509Certificate2>();
        }
    }

    private static string? DefaultPath()
    {
        var dir = AppContext.BaseDirectory;
        var candidate = Path.Combine(dir, "fido-mds3-roots.json");
        if (File.Exists(candidate)) return candidate;
        // Fall back to the source-tree location for tests / dev runs.
        var here = Path.GetDirectoryName(typeof(EmbeddedFidoMdsMetadataSource).Assembly.Location);
        if (!string.IsNullOrEmpty(here))
        {
            var sibling = Path.Combine(here!, "fido-mds3-roots.json");
            if (File.Exists(sibling)) return sibling;
        }
        return null;
    }
}

/// <summary>
/// Iter-35 — live MDS3 BLOB ingestion stub. Disabled unless
/// <c>RADIOPAD_FIDO_MDS3_URL</c> is set. The real implementation must
/// verify the JWT signature against the FIDO Alliance trust anchor; this
/// placeholder intentionally refuses to return roots until that wiring
/// lands so we never trust an unverified BLOB.
/// </summary>
public sealed class HttpFidoMdsMetadataSource : IFidoMdsMetadataSource
{
    private readonly HttpClient _http;
    public HttpFidoMdsMetadataSource(HttpClient http) { _http = http; }

    public IReadOnlyList<X509Certificate2> GetTrustedRoots()
    {
        // Live MDS3 ingestion is gated on JWT verification (FIDO Alliance
        // root JWS signing key + nbf/exp checks). Until that lands we
        // refuse to surface roots so the chain validator falls back to
        // the embedded source.
        return Array.Empty<X509Certificate2>();
    }
}
