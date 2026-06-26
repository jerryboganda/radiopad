using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace RadioPad.Application.Services.WebAuthn;

/// <summary>
/// AUTH-001 — Minimal WebAuthn attestation parser/verifier supporting
/// the three attestation formats RadioPad accepts at registration:
/// <list type="bullet">
/// <item><c>none</c> — no attestation, credential public key from authData is trusted as-is.</item>
/// <item><c>packed</c> — verifies <c>attStmt.sig</c> over <c>authData || clientDataHash</c>
/// using either the leaf X.509 cert in <c>x5c</c> or, for self-attestation,
/// the credential public key embedded in <c>authData</c>. Supports COSE
/// algorithms ES256 (-7) and RS256 (-257).</item>
/// <item><c>fido-u2f</c> — verifies the ECDSA-SHA256 signature over
/// <c>0x00 || rpIdHash || clientDataHash || credentialId || publicKeyU2F</c>
/// against the U2F attestation certificate.</item>
/// </list>
/// All other formats (<c>tpm</c>, <c>android-key</c>, <c>android-safetynet</c>,
/// <c>apple</c>, …) are reported as unsupported and rejected upstream.
///
/// The implementation is dependency-free (no Fido2NetLib) so it can run in
/// hermetic build environments. CBOR decoding is hand-rolled and limited
/// to the integer / byte string / text string / array / map subset that
/// WebAuthn attestation actually uses.
/// </summary>
public static class AttestationParser
{
    public sealed record Result(
        string Format,
        byte[] CredentialId,
        byte[] CosePublicKey,
        uint SignCount,
        byte[] AuthenticatorData,
        byte[] RpIdHash);

    public sealed class AttestationException : Exception
    {
        public string Kind { get; }
        public AttestationException(string kind, string message) : base(message) { Kind = kind; }
    }

    /// <summary>
    /// Parses and verifies the attestation object. Returns the credential
    /// id and COSE_Key public key on success; throws
    /// <see cref="AttestationException"/> on any verification failure or
    /// malformed input.
    /// </summary>
    /// <param name="mdsRoots">
    /// Iter-35 — optional FIDO MDS3 trusted-root source. When supplied, a
    /// packed attestation that ships an <c>x5c</c> chain must terminate in
    /// one of the returned roots; otherwise the chain is rejected with
    /// <c>Kind = "attestation_root"</c>. The <c>none</c> and <c>fido-u2f</c>
    /// paths are intentionally untouched.
    /// </param>
    public static Result Verify(byte[] attestationObject, byte[] clientDataJson, IFidoMdsMetadataSource? mdsRoots = null)
    {
        if (attestationObject is null || attestationObject.Length == 0)
            throw new AttestationException("validation", "attestationObject is empty.");
        if (clientDataJson is null || clientDataJson.Length == 0)
            throw new AttestationException("validation", "clientDataJson is empty.");

        var clientDataHash = SHA256.HashData(clientDataJson);

        var reader = new CborReader(attestationObject);
        var root = reader.ReadValue();
        if (root is not Dictionary<object, object> map)
            throw new AttestationException("validation", "attestationObject must be a CBOR map.");

        if (!map.TryGetValue("fmt", out var fmtObj) || fmtObj is not string fmt)
            throw new AttestationException("validation", "attestationObject missing fmt.");
        if (!map.TryGetValue("authData", out var authDataObj) || authDataObj is not byte[] authData)
            throw new AttestationException("validation", "attestationObject missing authData.");
        if (!map.TryGetValue("attStmt", out var attStmtObj) || attStmtObj is not Dictionary<object, object> attStmt)
            throw new AttestationException("validation", "attestationObject missing attStmt.");

        var supported = fmt is "none" or "packed" or "fido-u2f";
        if (!supported)
            throw new AttestationException("unsupported_format", $"Unsupported attestation format: {fmt}.");

        var ad = ParseAuthenticatorData(authData);

        switch (fmt)
        {
            case "none":
                if (attStmt.Count != 0)
                    throw new AttestationException("validation", "fmt=none must have empty attStmt.");
                break;
            case "packed":
                VerifyPacked(attStmt, authData, clientDataHash, ad.CosePublicKey, mdsRoots);
                break;
            case "fido-u2f":
                VerifyFidoU2f(attStmt, ad.RpIdHash, clientDataHash, ad.CredentialId, ad.CosePublicKey);
                break;
        }

        return new Result(fmt, ad.CredentialId, ad.CosePublicKey, ad.SignCount, authData, ad.RpIdHash);
    }

    /// <summary>
    /// AUTH-001 — verifies a WebAuthn assertion signature at sign-in time.
    /// The authenticator signs <c>authenticatorData || SHA-256(clientDataJSON)</c>
    /// with the private key whose COSE_Key public half we stored at
    /// registration. The COSE algorithm is read from the key itself (label 3),
    /// falling back to the key type (label 1) — ES256 (-7) for EC2, RS256
    /// (-257) for RSA. Throws <see cref="AttestationException"/> (Kind
    /// <c>"signature"</c> on a bad signature) on any failure.
    /// </summary>
    public static void VerifyAssertionSignature(
        byte[] cosePublicKey, byte[] authenticatorData, byte[] clientDataJson, byte[] signature)
    {
        if (cosePublicKey is null || cosePublicKey.Length == 0)
            throw new AttestationException("validation", "stored credentialPublicKey is empty.");
        if (authenticatorData is null || authenticatorData.Length < 37)
            throw new AttestationException("validation", "authenticatorData too short.");
        if (clientDataJson is null || clientDataJson.Length == 0)
            throw new AttestationException("validation", "clientDataJson is empty.");
        if (signature is null || signature.Length == 0)
            throw new AttestationException("validation", "assertion signature is empty.");

        var clientDataHash = SHA256.HashData(clientDataJson);
        var signed = new byte[authenticatorData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authenticatorData, 0, signed, 0, authenticatorData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signed, authenticatorData.Length, clientDataHash.Length);

        var cose = new CborReader(cosePublicKey).ReadValue() as Dictionary<object, object>
            ?? throw new AttestationException("validation", "stored credentialPublicKey is not a CBOR map.");
        int alg;
        if (cose.TryGetValue(3L, out var algObj) && algObj is long a)
            alg = (int)a;
        else if (cose.TryGetValue(1L, out var ktyObj) && ktyObj is long kty)
            alg = kty == 3 ? -257 : -7; // 3 = RSA, 2 = EC2
        else
            alg = -7;

        VerifySignatureWithCose(cosePublicKey, alg, signed, signature);
    }

    /// <summary>
    /// AUTH-001 — validates the <c>clientDataJSON</c> collected by the browser
    /// against the ceremony we initiated: the <c>type</c> must match
    /// (<c>webauthn.create</c> for registration, <c>webauthn.get</c> for
    /// sign-in), the <c>challenge</c> must equal the single-use value we issued
    /// (compared constant-time), and the <c>origin</c> must be allowed. When
    /// <paramref name="allowedOrigins"/> is empty the origin's host must equal
    /// <paramref name="rpId"/> or be a sub-domain of it (WebAuthn §7).
    /// </summary>
    public static void VerifyClientData(
        byte[] clientDataJson, string expectedType, string expectedChallengeB64Url,
        IReadOnlyCollection<string>? allowedOrigins, string rpId)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(clientDataJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AttestationException("validation", "clientDataJSON is not valid JSON.");
        }

        if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() is not string type)
            throw new AttestationException("validation", "clientDataJSON missing type.");
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
            throw new AttestationException("validation", $"clientDataJSON.type must be {expectedType}.");

        if (!root.TryGetProperty("challenge", out var chEl) || chEl.GetString() is not string challenge)
            throw new AttestationException("validation", "clientDataJSON missing challenge.");
        if (!ConstantTimeEquals(NormalizeB64Url(challenge), NormalizeB64Url(expectedChallengeB64Url)))
            throw new AttestationException("validation", "clientDataJSON.challenge does not match the issued challenge.");

        if (!root.TryGetProperty("origin", out var orEl) || orEl.GetString() is not string origin)
            throw new AttestationException("validation", "clientDataJSON missing origin.");
        if (!IsOriginAllowed(origin, allowedOrigins, rpId))
            throw new AttestationException("validation", "clientDataJSON.origin is not an allowed origin.");
    }

    /// <summary>
    /// AUTH-001 — parses and validates the assertion <c>authenticatorData</c>:
    /// the rpIdHash must equal SHA-256(rpId), the User Present (UP) flag must be
    /// set, and — when <paramref name="requireUserVerification"/> — the User
    /// Verified (UV) flag must be set. Returns the parsed signCount for the
    /// caller's clone-detection check.
    /// </summary>
    public static (byte[] rpIdHash, byte flags, uint signCount) VerifyAssertionAuthData(
        byte[] authData, string rpId, bool requireUserVerification)
    {
        if (authData is null || authData.Length < 37)
            throw new AttestationException("validation", "authenticatorData too short.");
        var rpIdHash = authData[..32];
        var flags = authData[32];
        var signCount = (uint)((authData[33] << 24) | (authData[34] << 16) | (authData[35] << 8) | authData[36]);

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
        if (!CryptographicOperations.FixedTimeEquals(rpIdHash, expected))
            throw new AttestationException("validation", "authenticatorData rpIdHash does not match the RP id.");
        const byte UP = 0x01, UV = 0x04;
        if ((flags & UP) == 0)
            throw new AttestationException("validation", "authenticatorData User Present (UP) flag not set.");
        if (requireUserVerification && (flags & UV) == 0)
            throw new AttestationException("validation", "authenticatorData User Verified (UV) flag required but not set.");
        return (rpIdHash, flags, signCount);
    }

    private static bool IsOriginAllowed(string origin, IReadOnlyCollection<string>? allowedOrigins, string rpId)
    {
        if (allowedOrigins is { Count: > 0 })
            return allowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host;
        return string.Equals(host, rpId, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + rpId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeB64Url(string s) => s.Replace("=", "").Replace('+', '-').Replace('/', '_');

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    // -- authData -----------------------------------------------------------

    private sealed record AuthData(
        byte[] RpIdHash,
        byte Flags,
        uint SignCount,
        byte[] CredentialId,
        byte[] CosePublicKey);

    private static AuthData ParseAuthenticatorData(byte[] authData)
    {
        if (authData.Length < 37)
            throw new AttestationException("validation", "authData too short.");
        var rpIdHash = authData[..32];
        var flags = authData[32];
        var signCount = (uint)((authData[33] << 24) | (authData[34] << 16) | (authData[35] << 8) | authData[36]);
        const byte AT = 0x40;
        if ((flags & AT) == 0)
            throw new AttestationException("validation", "authData missing attested credential data (AT flag).");
        var offset = 37;
        if (authData.Length < offset + 18)
            throw new AttestationException("validation", "authData truncated before AAGUID.");
        // Skip 16-byte AAGUID.
        offset += 16;
        var credIdLen = (authData[offset] << 8) | authData[offset + 1];
        offset += 2;
        if (credIdLen <= 0 || credIdLen > 1023 || authData.Length < offset + credIdLen)
            throw new AttestationException("validation", "authData has invalid credentialId length.");
        var credentialId = authData.AsSpan(offset, credIdLen).ToArray();
        offset += credIdLen;
        // The remainder is the COSE_Key (CBOR map). Determine its byte length
        // by re-reading from the slice.
        var coseSpan = authData.AsSpan(offset);
        var coseLen = CborReader.MeasureLength(coseSpan);
        if (coseLen <= 0)
            throw new AttestationException("validation", "authData has invalid credentialPublicKey.");
        var cose = coseSpan[..coseLen].ToArray();
        return new AuthData(rpIdHash, flags, signCount, credentialId, cose);
    }

    // -- packed -------------------------------------------------------------

    private static void VerifyPacked(
        Dictionary<object, object> attStmt,
        byte[] authData,
        byte[] clientDataHash,
        byte[] cosePublicKey,
        IFidoMdsMetadataSource? mdsRoots)
    {
        if (!attStmt.TryGetValue("alg", out var algObj) || algObj is not long alg)
            throw new AttestationException("validation", "packed.attStmt missing alg.");
        if (!attStmt.TryGetValue("sig", out var sigObj) || sigObj is not byte[] sig)
            throw new AttestationException("validation", "packed.attStmt missing sig.");

        var signed = new byte[authData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authData, 0, signed, 0, authData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signed, authData.Length, clientDataHash.Length);

        if (attStmt.TryGetValue("x5c", out var x5cObj) && x5cObj is List<object> x5cList && x5cList.Count > 0)
        {
            var chain = x5cList.Select(c => c is byte[] b
                ? new X509Certificate2(b)
                : throw new AttestationException("validation", "x5c entries must be byte strings."))
                .ToList();
            VerifyChain(chain);
            // Iter-35 — pin packed-with-x5c chains to the FIDO MDS3 trusted
            // root set when an MDS source is supplied. `none` / `fido-u2f`
            // remain untouched so existing test vectors keep working.
            if (mdsRoots is not null)
                VerifyChainTerminatesInTrustedRoot(chain, mdsRoots);
            VerifySignatureWithCert(chain[0], (int)alg, signed, sig);
        }
        else
        {
            VerifySignatureWithCose(cosePublicKey, (int)alg, signed, sig);
        }
    }

    // -- fido-u2f -----------------------------------------------------------

    private static void VerifyFidoU2f(
        Dictionary<object, object> attStmt,
        byte[] rpIdHash,
        byte[] clientDataHash,
        byte[] credentialId,
        byte[] cosePublicKey)
    {
        if (!attStmt.TryGetValue("sig", out var sigObj) || sigObj is not byte[] sig)
            throw new AttestationException("validation", "fido-u2f.attStmt missing sig.");
        if (!attStmt.TryGetValue("x5c", out var x5cObj) || x5cObj is not List<object> x5cList || x5cList.Count == 0)
            throw new AttestationException("validation", "fido-u2f.attStmt missing x5c.");

        var chain = x5cList.Select(c => c is byte[] b
            ? new X509Certificate2(b)
            : throw new AttestationException("validation", "x5c entries must be byte strings."))
            .ToList();
        VerifyChain(chain);

        // Reconstruct U2F public key (0x04 || x || y) from COSE_Key.
        var cose = new CborReader(cosePublicKey).ReadValue() as Dictionary<object, object>
            ?? throw new AttestationException("validation", "credentialPublicKey is not a CBOR map.");
        if (!cose.TryGetValue(-2L, out var xObj) || xObj is not byte[] x || x.Length != 32)
            throw new AttestationException("validation", "fido-u2f credentialPublicKey missing x (32 bytes).");
        if (!cose.TryGetValue(-3L, out var yObj) || yObj is not byte[] y || y.Length != 32)
            throw new AttestationException("validation", "fido-u2f credentialPublicKey missing y (32 bytes).");
        var u2fPub = new byte[1 + 32 + 32];
        u2fPub[0] = 0x04;
        Buffer.BlockCopy(x, 0, u2fPub, 1, 32);
        Buffer.BlockCopy(y, 0, u2fPub, 33, 32);

        var signed = new byte[1 + rpIdHash.Length + clientDataHash.Length + credentialId.Length + u2fPub.Length];
        var i = 0;
        signed[i++] = 0x00;
        Buffer.BlockCopy(rpIdHash, 0, signed, i, rpIdHash.Length); i += rpIdHash.Length;
        Buffer.BlockCopy(clientDataHash, 0, signed, i, clientDataHash.Length); i += clientDataHash.Length;
        Buffer.BlockCopy(credentialId, 0, signed, i, credentialId.Length); i += credentialId.Length;
        Buffer.BlockCopy(u2fPub, 0, signed, i, u2fPub.Length);

        using var leafEcdsa = chain[0].GetECDsaPublicKey()
            ?? throw new AttestationException("validation", "fido-u2f leaf cert is not ECDSA.");
        var ok = leafEcdsa.VerifyData(signed, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (!ok) throw new AttestationException("signature", "fido-u2f signature verification failed.");
    }

    // -- shared verification helpers ---------------------------------------

    private static void VerifyChain(List<X509Certificate2> chain)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < chain.Count; i++)
        {
            var cert = chain[i];
            if (now < cert.NotBefore || now > cert.NotAfter)
                throw new AttestationException("certificate", "x5c cert outside validity period.");
            if (i > 0 && !string.Equals(chain[i - 1].Issuer, cert.Subject, StringComparison.Ordinal))
                throw new AttestationException("certificate", "x5c chain is broken (Issuer != prev.Subject).");
        }
    }

    /// <summary>
    /// Iter-35 — verifies that the supplied x5c chain terminates in one of
    /// the FIDO MDS3 trusted roots. The chain itself does not have to
    /// include the root (per WebAuthn §8.2 the AAGUID lookup yields the
    /// expected root); we accept either:
    /// <list type="bullet">
    /// <item>the last cert in <paramref name="chain"/> is itself a trusted
    /// root (Subject == Issuer and matches a root by raw byte equality),
    /// or</item>
    /// <item>the last cert's Issuer matches one of the trusted roots'
    /// Subject and the trusted root verifies the last cert's signature.</item>
    /// </list>
    /// On failure throws <see cref="AttestationException"/> with
    /// <c>Kind = "attestation_root"</c> so the controller can audit a
    /// <c>PolicyViolation{kind:"webauthn_attestation_root"}</c>.
    /// </summary>
    private static void VerifyChainTerminatesInTrustedRoot(
        List<X509Certificate2> chain,
        IFidoMdsMetadataSource source)
    {
        var roots = source.GetTrustedRoots();
        if (roots.Count == 0)
            throw new AttestationException("attestation_root",
                "FIDO MDS3 trusted-root set is empty; packed attestation with x5c chain refused.");

        var tail = chain[^1];
        // Case 1 — tail is itself a trusted root (raw bytes match).
        var tailBytes = tail.RawData;
        foreach (var r in roots)
        {
            if (tailBytes.Length == r.RawData.Length && tailBytes.AsSpan().SequenceEqual(r.RawData))
                return;
        }
        // Case 2 — tail's Issuer == one of the roots' Subject, and the
        // root's public key verifies the tail's signature.
        foreach (var r in roots)
        {
            if (!string.Equals(tail.Issuer, r.Subject, StringComparison.Ordinal)) continue;
            try
            {
                using var chainPolicy = new X509Chain
                {
                    ChainPolicy =
                    {
                        RevocationMode = X509RevocationMode.NoCheck,
                        TrustMode = X509ChainTrustMode.CustomRootTrust,
                        VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority,
                    },
                };
                chainPolicy.ChainPolicy.CustomTrustStore.Add(r);
                foreach (var c in chain) chainPolicy.ChainPolicy.ExtraStore.Add(c);
                if (chainPolicy.Build(tail))
                    return;
            }
            catch
            {
                /* fall through to next candidate root */
            }
        }
        throw new AttestationException("attestation_root",
            "Packed attestation x5c chain does not terminate in a FIDO MDS3 trusted root.");
    }

    private static void VerifySignatureWithCert(X509Certificate2 cert, int alg, byte[] data, byte[] sig)
    {
        switch (alg)
        {
            case -7:
            {
                using var ecdsa = cert.GetECDsaPublicKey()
                    ?? throw new AttestationException("validation", "alg=-7 requires ECDSA cert.");
                if (!ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    throw new AttestationException("signature", "packed signature verification failed.");
                break;
            }
            case -257:
            {
                using var rsa = cert.GetRSAPublicKey()
                    ?? throw new AttestationException("validation", "alg=-257 requires RSA cert.");
                if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new AttestationException("signature", "packed signature verification failed.");
                break;
            }
            default:
                throw new AttestationException("validation", $"Unsupported COSE alg: {alg}.");
        }
    }

    private static void VerifySignatureWithCose(byte[] cosePublicKey, int alg, byte[] data, byte[] sig)
    {
        var cose = new CborReader(cosePublicKey).ReadValue() as Dictionary<object, object>
            ?? throw new AttestationException("validation", "credentialPublicKey is not a CBOR map.");
        switch (alg)
        {
            case -7:
            {
                if (!cose.TryGetValue(-2L, out var xObj) || xObj is not byte[] x)
                    throw new AttestationException("validation", "ES256 COSE_Key missing x.");
                if (!cose.TryGetValue(-3L, out var yObj) || yObj is not byte[] y)
                    throw new AttestationException("validation", "ES256 COSE_Key missing y.");
                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = x, Y = y },
                });
                if (!ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    throw new AttestationException("signature", "packed self-attestation signature verification failed.");
                break;
            }
            case -257:
            {
                if (!cose.TryGetValue(-1L, out var nObj) || nObj is not byte[] n)
                    throw new AttestationException("validation", "RS256 COSE_Key missing n.");
                if (!cose.TryGetValue(-2L, out var eObj) || eObj is not byte[] e)
                    throw new AttestationException("validation", "RS256 COSE_Key missing e.");
                using var rsa = RSA.Create(new RSAParameters { Modulus = n, Exponent = e });
                if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new AttestationException("signature", "packed self-attestation signature verification failed.");
                break;
            }
            default:
                throw new AttestationException("validation", $"Unsupported COSE alg: {alg}.");
        }
    }
}

/// <summary>
/// Minimal CBOR decoder covering the subset used by WebAuthn attestation:
/// unsigned int, negative int, byte string, text string, array, map.
/// Indefinite-length items, tags, simple values (other than the booleans
/// / null we don't expect), and floats are not supported.
/// </summary>
internal sealed class CborReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _pos;

    public CborReader(ReadOnlyMemory<byte> data) { _data = data; _pos = 0; }
    public CborReader(byte[] data) : this((ReadOnlyMemory<byte>)data) { }

    public int Position => _pos;

    /// <summary>Returns the byte length of the next CBOR item starting at offset 0 of <paramref name="span"/>.</summary>
    public static int MeasureLength(ReadOnlySpan<byte> span)
    {
        var copy = span.ToArray();
        var r = new CborReader(copy);
        try { r.ReadValue(); } catch { return -1; }
        return r._pos;
    }

    public object ReadValue()
    {
        if (_pos >= _data.Length)
            throw new AttestationParser.AttestationException("validation", "Unexpected end of CBOR input.");
        var ib = _data.Span[_pos++];
        var major = ib >> 5;
        var minor = ib & 0x1f;
        var arg = ReadArgument(minor);
        return major switch
        {
            0 => (long)arg,
            1 => -1L - (long)arg,
            2 => ReadBytes((int)arg),
            3 => Encoding.UTF8.GetString(ReadBytes((int)arg)),
            4 => ReadArray((int)arg),
            5 => ReadMap((int)arg),
            _ => throw new AttestationParser.AttestationException("validation", $"Unsupported CBOR major type {major}."),
        };
    }

    private ulong ReadArgument(int minor)
    {
        if (minor < 24) return (ulong)minor;
        return minor switch
        {
            24 => ReadUInt(1),
            25 => ReadUInt(2),
            26 => ReadUInt(4),
            27 => ReadUInt(8),
            _ => throw new AttestationParser.AttestationException("validation", $"Unsupported CBOR argument {minor}."),
        };
    }

    private ulong ReadUInt(int n)
    {
        if (_pos + n > _data.Length)
            throw new AttestationParser.AttestationException("validation", "Truncated CBOR argument.");
        ulong v = 0;
        for (var i = 0; i < n; i++) v = (v << 8) | _data.Span[_pos++];
        return v;
    }

    private byte[] ReadBytes(int len)
    {
        if (len < 0 || _pos + len > _data.Length)
            throw new AttestationParser.AttestationException("validation", "Truncated CBOR byte/text string.");
        var arr = _data.Slice(_pos, len).ToArray();
        _pos += len;
        return arr;
    }

    private List<object> ReadArray(int n)
    {
        var list = new List<object>(n);
        for (var i = 0; i < n; i++) list.Add(ReadValue());
        return list;
    }

    private Dictionary<object, object> ReadMap(int n)
    {
        var d = new Dictionary<object, object>(n);
        for (var i = 0; i < n; i++)
        {
            var k = ReadValue();
            var v = ReadValue();
            d[k] = v;
        }
        return d;
    }
}
