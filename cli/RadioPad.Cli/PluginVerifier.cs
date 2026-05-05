// PRD DESK-009 / CLI-PLUGIN — mirrors `desktop/src-tauri/src/sandbox.rs`:
//   1. SHA-256 (constant-time) compare against expected hex digest.
//   2. Optional Ed25519 detached signature verification against a key from
//      RADIOPAD_PLUGIN_PUBKEY (PEM or 32-byte hex).
//
// Build pipelines call this so artifacts are validated *before* they reach
// a workstation. Consistent semantics with the desktop sandbox is essential.

using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace RadioPad.Cli;

internal static class PluginVerifier
{
    public static void Verify(string path, string expectedSha256Hex, string? signatureB64OrHex)
    {
        var bytes = File.ReadAllBytes(path);

        // 1. SHA-256 hash check (constant-time compare).
        var expected = HexDecode(expectedSha256Hex)
            ?? throw new InvalidOperationException("expected sha256 is not valid hex");
        var actual = SHA256.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidOperationException("sha256 mismatch");
        }

        // 2. Signature check.
        var pem = Environment.GetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY");
        var hasSig = !string.IsNullOrWhiteSpace(signatureB64OrHex);
        var hasKey = !string.IsNullOrWhiteSpace(pem);

        if (hasSig && hasKey)
        {
            var sig = TryDecode(signatureB64OrHex!)
                ?? throw new InvalidOperationException("signature is not valid base64/hex");
            if (sig.Length != 64)
            {
                throw new InvalidOperationException("signature must be 64 bytes");
            }
            var pubkey = ParseEd25519PublicKey(pem!);
            var verifier = new Ed25519Signer();
            verifier.Init(false, pubkey);
            verifier.BlockUpdate(bytes, 0, bytes.Length);
            if (!verifier.VerifySignature(sig))
            {
                throw new InvalidOperationException("ed25519 verification failed");
            }
        }
        else if (hasSig && !hasKey)
        {
            throw new InvalidOperationException("RADIOPAD_PLUGIN_PUBKEY not set");
        }
        else if (!hasSig && hasKey)
        {
            throw new InvalidOperationException("signature required because RADIOPAD_PLUGIN_PUBKEY is set");
        }
        else
        {
            // No signature, no key: warn (CI build artefact path may not be
            // signed yet during bring-up). Mirrors the desktop debug-build
            // behaviour so dev workflows aren't blocked.
            Console.Error.WriteLine(
                $"WARN  {Path.GetFileName(path)} loaded without signature (RADIOPAD_PLUGIN_PUBKEY unset)");
        }
    }

    private static Ed25519PublicKeyParameters ParseEd25519PublicKey(string input)
    {
        var trimmed = input.Trim();

        // 32-byte hex form first.
        var hex = HexDecode(trimmed);
        if (hex is { Length: 32 })
        {
            return new Ed25519PublicKeyParameters(hex, 0);
        }

        // PEM SubjectPublicKeyInfo: take the trailing 32 bytes of the DER
        // body — RFC 8410 says the BIT STRING (the raw key) is the last
        // component of the SPKI structure.
        if (trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal))
        {
            var inner = string.Concat(trimmed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("-----", StringComparison.Ordinal)));
            var der = Convert.FromBase64String(inner);
            if (der.Length < 32)
            {
                throw new InvalidOperationException("PEM body too short for Ed25519 SPKI");
            }
            var raw = new byte[32];
            Buffer.BlockCopy(der, der.Length - 32, raw, 0, 32);
            return new Ed25519PublicKeyParameters(raw, 0);
        }

        throw new InvalidOperationException("RADIOPAD_PLUGIN_PUBKEY must be PEM or 32-byte hex");
    }

    private static byte[]? TryDecode(string s)
    {
        // base64 first, then hex.
        try { return Convert.FromBase64String(s.Trim()); }
        catch { /* fall through */ }
        return HexDecode(s);
    }

    private static byte[]? HexDecode(string s)
    {
        var t = s.Trim();
        if (t.Length % 2 != 0) return null;
        var bytes = new byte[t.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(t.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
            {
                return null;
            }
        }
        return bytes;
    }
}
