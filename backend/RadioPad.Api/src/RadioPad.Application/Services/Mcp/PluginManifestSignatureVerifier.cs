using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-33 MCP-007 — verifies a plugin's <c>manifest.json.sig</c> against
/// the canonical-JSON serialisation of <c>manifest.json</c> using ed25519
/// detached signatures. The accepted public keys come from the tenant's
/// <see cref="TrustedPluginPublisher"/> table; revoked rows are ignored.
/// On verification failure the service appends an
/// <see cref="AuditAction.ProviderBlocked"/> row before throwing
/// <see cref="PluginPolicyException"/> so the host can never silently
/// load an unsigned / tampered plugin.
/// </summary>
public sealed class PluginManifestSignatureVerifier
{
    private readonly IAuditLog _audit;

    public PluginManifestSignatureVerifier(IAuditLog audit)
    {
        _audit = audit;
    }

    /// <summary>
    /// Canonicalise an arbitrary JSON string: sort object keys
    /// case-sensitive ordinal, drop whitespace, normalise number forms via
    /// <see cref="JsonElement"/> round-trip. Identical input documents
    /// produce identical output bytes regardless of original key order or
    /// indentation, which is what the signer must hash.
    /// </summary>
    public static byte[] Canonicalize(string manifestJson)
    {
        if (manifestJson is null) throw new ArgumentNullException(nameof(manifestJson));
        using var doc = JsonDocument.Parse(manifestJson);
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteCanonical(w, doc.RootElement);
        }
        return ms.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter w, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    w.WritePropertyName(p.Name);
                    WriteCanonical(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in el.EnumerateArray()) WriteCanonical(w, item);
                w.WriteEndArray();
                break;
            case JsonValueKind.String:
                w.WriteStringValue(el.GetString());
                break;
            case JsonValueKind.Number:
                // Round-trip via raw text to preserve the canonical form.
                w.WriteRawValue(el.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                w.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                w.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                w.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {el.ValueKind}");
        }
    }

    /// <summary>
    /// Verify <paramref name="signatureBytes"/> against the canonical bytes
    /// of <paramref name="manifestJson"/> using each of the supplied
    /// non-revoked publisher keys. The first key that verifies wins.
    /// On failure the method audits <see cref="AuditAction.ProviderBlocked"/>
    /// and throws <see cref="PluginPolicyException"/>.
    /// </summary>
    public async Task<TrustedPluginPublisher> VerifyAsync(
        Guid tenantId,
        Guid? userId,
        string pluginId,
        string manifestJson,
        byte[]? signatureBytes,
        IReadOnlyList<TrustedPluginPublisher> publishers,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId is required", nameof(pluginId));

        byte[] canonical;
        try
        {
            canonical = Canonicalize(manifestJson);
        }
        catch (JsonException ex)
        {
            await BlockAsync(tenantId, userId, pluginId, "bad_manifest_json", ct).ConfigureAwait(false);
            throw new PluginPolicyException(pluginId, "bad_manifest_json",
                $"Plugin '{pluginId}' manifest is not valid JSON.", ex);
        }

        if (signatureBytes is null || signatureBytes.Length != 64)
        {
            await BlockAsync(tenantId, userId, pluginId, "missing_signature", ct).ConfigureAwait(false);
            throw new PluginPolicyException(pluginId, "missing_signature",
                $"Plugin '{pluginId}' manifest.json.sig is missing or wrong length.");
        }

        var active = publishers
            .Where(p => p.TenantId == tenantId && p.RevokedAt is null)
            .Where(p => !string.IsNullOrWhiteSpace(p.Ed25519PublicKeyBase64))
            .ToArray();

        if (active.Length == 0)
        {
            await BlockAsync(tenantId, userId, pluginId, "no_trusted_publisher", ct).ConfigureAwait(false);
            throw new PluginPolicyException(pluginId, "no_trusted_publisher",
                $"Plugin '{pluginId}' rejected — tenant has no active TrustedPluginPublisher key.");
        }

        foreach (var pub in active)
        {
            byte[] keyBytes;
            try { keyBytes = Convert.FromBase64String(pub.Ed25519PublicKeyBase64); }
            catch (FormatException) { continue; }
            if (keyBytes.Length != 32) continue;

            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(false, new Ed25519PublicKeyParameters(keyBytes, 0));
                verifier.BlockUpdate(canonical, 0, canonical.Length);
                if (verifier.VerifySignature(signatureBytes))
                {
                    return pub;
                }
            }
            catch
            {
                // try next key
            }
        }

        await BlockAsync(tenantId, userId, pluginId, "bad_signature", ct).ConfigureAwait(false);
        throw new PluginPolicyException(pluginId, "bad_signature",
            $"Plugin '{pluginId}' manifest signature did not verify against any trusted publisher key.");
    }

    private Task BlockAsync(Guid tenantId, Guid? userId, string pluginId, string reason, CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new
        {
            kind = "plugin_policy",
            pluginId,
            reason,
        });
        var evt = new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.ProviderBlocked,
            DetailsJson = details,
        };
        return _audit.AppendAsync(evt, ct);
    }

    /// <summary>
    /// Helper for callers (and tests) that need the canonical SHA-256
    /// fingerprint of a manifest body without performing verification.
    /// </summary>
    public static string FingerprintHex(string manifestJson)
    {
        var bytes = Canonicalize(manifestJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>UTF-8 hash convenience for tests.</summary>
    public static string FingerprintHex(byte[] canonicalBytes)
    {
        var hash = SHA256.HashData(canonicalBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Used by sign-tooling examples in <c>desktop/PLUGIN_TRUST.md</c>.</summary>
    public static string CanonicalizeToString(string manifestJson)
        => Encoding.UTF8.GetString(Canonicalize(manifestJson));
}
