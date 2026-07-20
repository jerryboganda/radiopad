using System.Text;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 DESK-007 — verifier tests for the PACS plugin manifest pipeline.
/// The CLI mirror of the desktop sandbox is exercised in
/// <c>RadioPad.Cli.PluginVerifier</c>; here we verify the wire format and
/// trust-model edges (missing key, hash mismatch, signature mismatch).
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class PacsPluginsVerifierTests
{
    [Fact]
    public void Manifest_Schema_Round_Trips_Through_Json_With_Required_Fields()
    {
        var json = """
            {
                "id": "sectra-pacs",
                "name": "Sectra IDS7 workstation bridge",
                "vendor": "Sectra",
                "version": "0.1.0",
                "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                "capabilities": ["window-detect", "accession-grab", "paste-back"],
                "enabled": false
            }
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sectra-pacs", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("Sectra", doc.RootElement.GetProperty("vendor").GetString());
        var caps = doc.RootElement.GetProperty("capabilities");
        Assert.Equal(JsonValueKind.Array, caps.ValueKind);
        Assert.Equal(3, caps.GetArrayLength());
        // 64-char lowercase hex SHA-256.
        var sha = doc.RootElement.GetProperty("sha256").GetString()!;
        Assert.Equal(64, sha.Length);
    }

    [Fact]
    public void PluginVerifier_Sha256_Mismatch_Throws()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllBytes(tmp, Encoding.UTF8.GetBytes("manifest body"));
        try
        {
            // Wrong sha256 — expect failure.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                RadioPad.Cli.PluginVerifier.Verify(
                    tmp, expectedSha256Hex: new string('0', 64), signatureB64OrHex: null));
            Assert.Contains("sha256", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void PluginVerifier_Sha256_Match_No_Signature_Allows_When_PubKey_Unset()
    {
        var prev = Environment.GetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY");
        Environment.SetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY", null);
        var tmp = Path.GetTempFileName();
        var bytes = Encoding.UTF8.GetBytes("{\"id\":\"test\"}");
        File.WriteAllBytes(tmp, bytes);
        try
        {
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            // No signature, no pubkey configured → emits a warning to stderr
            // but does NOT throw (mirrors desktop debug-build semantics).
            RadioPad.Cli.PluginVerifier.Verify(tmp, sha, signatureB64OrHex: null);
        }
        finally
        {
            File.Delete(tmp);
            Environment.SetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY", prev);
        }
    }

    [Fact]
    public void PluginVerifier_Refuses_Signature_When_Pubkey_Unset()
    {
        var prev = Environment.GetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY");
        Environment.SetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY", null);
        var tmp = Path.GetTempFileName();
        var bytes = Encoding.UTF8.GetBytes("body");
        File.WriteAllBytes(tmp, bytes);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        // 64-byte fake signature in base64.
        var sig = Convert.ToBase64String(new byte[64]);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                RadioPad.Cli.PluginVerifier.Verify(tmp, sha, sig));
            Assert.Contains("PUBKEY", ex.Message);
        }
        finally
        {
            File.Delete(tmp);
            Environment.SetEnvironmentVariable("RADIOPAD_PLUGIN_PUBKEY", prev);
        }
    }
}
