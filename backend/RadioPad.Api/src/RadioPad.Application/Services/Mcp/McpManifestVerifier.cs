using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-32 MCP-007 — verifies an Ed25519 detached signature over a tool
/// manifest's UTF-8 bytes against the configured RadioPad release public
/// key. The release public key is loaded from
/// <c>RADIOPAD_MCP_RELEASE_PUBKEY_B64</c> (32-byte raw key, base64) or, in
/// dev/test, from the placeholder file
/// <c>mcp-connectors/_signing/release.pub</c> in the repo root.
/// </summary>
public sealed class McpManifestVerifier
{
    public sealed record VerifyResult(bool Valid, string Sha256, string? Error);

    /// <summary>
    /// Verify the detached <paramref name="signatureB64"/> against
    /// <paramref name="manifestBytes"/> using the supplied public key.
    /// Always returns the SHA-256 of the manifest so the registry can store
    /// it regardless of whether the signature validated.
    /// </summary>
    public VerifyResult Verify(byte[] manifestBytes, string signatureB64, byte[] publicKey32)
    {
        var sha = ComputeSha256(manifestBytes);
        if (string.IsNullOrWhiteSpace(signatureB64))
            return new VerifyResult(false, sha, "missing_signature");
        if (publicKey32 is null || publicKey32.Length != 32)
            return new VerifyResult(false, sha, "missing_public_key");
        byte[] sig;
        try { sig = Convert.FromBase64String(signatureB64); }
        catch (FormatException) { return new VerifyResult(false, sha, "bad_signature_b64"); }
        if (sig.Length != 64)
            return new VerifyResult(false, sha, "bad_signature_length");
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey32, 0));
            verifier.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
            return verifier.VerifySignature(sig)
                ? new VerifyResult(true, sha, null)
                : new VerifyResult(false, sha, "bad_signature");
        }
        catch (Exception ex)
        {
            return new VerifyResult(false, sha, $"verifier_error:{ex.GetType().Name}");
        }
    }

    public VerifyResult Verify(string manifestJson, string signatureB64, byte[] publicKey32)
        => Verify(Encoding.UTF8.GetBytes(manifestJson ?? ""), signatureB64, publicKey32);

    public static string ComputeSha256(byte[] bytes)
    {
        var h = SHA256.HashData(bytes);
        return Convert.ToHexString(h).ToLowerInvariant();
    }
}
