using System.Security.Cryptography.X509Certificates;

namespace RadioPad.Application.Services.WebAuthn;

/// <summary>
/// Iter-35 AUTH-001 — abstraction over the FIDO Alliance MDS3 metadata
/// blob. Returns the set of trusted root X.509 certificates that a packed
/// attestation's <c>x5c</c> chain must terminate in. Implementations:
/// <list type="bullet">
/// <item><see cref="EmbeddedFidoMdsMetadataSource"/> — reads a static
/// JSON file shipped with the build (<c>fido-mds3-roots.json</c>) listing
/// base64-DER-encoded root CAs from the FIDO Alliance MDS3 BLOB.</item>
/// <item><see cref="HttpFidoMdsMetadataSource"/> — fetches the live MDS3
/// JWT BLOB from <c>RADIOPAD_FIDO_MDS3_URL</c>. Disabled by default; the
/// JWT signature verification path is intentionally a placeholder until
/// an operator wires the FIDO MDS3 trust anchor.</item>
/// </list>
/// </summary>
public interface IFidoMdsMetadataSource
{
    /// <summary>Returns the trusted root certificates. Must be safe to call repeatedly.</summary>
    IReadOnlyList<X509Certificate2> GetTrustedRoots();
}
