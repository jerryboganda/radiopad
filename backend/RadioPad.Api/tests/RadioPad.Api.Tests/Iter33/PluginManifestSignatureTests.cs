using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 MCP-007 — verify the plugin manifest signature chain end-to-end:
/// signs a manifest body with an ephemeral ed25519 key, registers the
/// publisher, asserts the verifier accepts the signature, then corrupts a
/// byte and asserts the verifier blocks the load and writes a
/// <see cref="AuditAction.ProviderBlocked"/> row.
/// </summary>
public class PluginManifestSignatureTests : IClassFixture<RadioPad.Api.Tests.Integration.RadioPadAppFactory>
{
    private readonly RadioPad.Api.Tests.Integration.RadioPadAppFactory _factory;

    public PluginManifestSignatureTests(RadioPad.Api.Tests.Integration.RadioPadAppFactory factory)
        => _factory = factory;

    private static (Ed25519PrivateKeyParameters priv, Ed25519PublicKeyParameters pub) NewKeyPair()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var kp = gen.GenerateKeyPair();
        return ((Ed25519PrivateKeyParameters)kp.Private, (Ed25519PublicKeyParameters)kp.Public);
    }

    private static byte[] Sign(byte[] message, Ed25519PrivateKeyParameters priv)
    {
        var s = new Ed25519Signer();
        s.Init(true, priv);
        s.BlockUpdate(message, 0, message.Length);
        return s.GenerateSignature();
    }

    [Fact]
    public async Task ValidSignature_ReturnsPublisher()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var verifier = new PluginManifestSignatureVerifier(audit);

        var (priv, pub) = NewKeyPair();
        var pubB64 = Convert.ToBase64String(pub.GetEncoded());
        var publisher = new TrustedPluginPublisher
        {
            TenantId = _factory.SeedTenant.Id,
            PublisherName = $"sig-test-{Guid.NewGuid():N}",
            Ed25519PublicKeyBase64 = pubB64,
        };
        db.TrustedPluginPublishers.Add(publisher);
        await db.SaveChangesAsync();

        var manifestJson = "{\"id\":\"acme-pacs\",\"version\":\"1.0.0\",\"capabilities\":[\"dicomweb.read\",\"rulebook.lookup\"]}";
        var canonical = PluginManifestSignatureVerifier.Canonicalize(manifestJson);
        var sig = Sign(canonical, priv);

        var publishers = await db.TrustedPluginPublishers
            .Where(p => p.TenantId == _factory.SeedTenant.Id)
            .ToListAsync();

        var matched = await verifier.VerifyAsync(
            _factory.SeedTenant.Id,
            _factory.SeedUser.Id,
            "acme-pacs",
            manifestJson,
            sig,
            publishers,
            CancellationToken.None);

        Assert.Equal(publisher.Id, matched.Id);
    }

    [Fact]
    public async Task CorruptedManifest_FailsAndAudits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var verifier = new PluginManifestSignatureVerifier(audit);

        var (priv, pub) = NewKeyPair();
        var pubB64 = Convert.ToBase64String(pub.GetEncoded());
        var publisher = new TrustedPluginPublisher
        {
            TenantId = _factory.SeedTenant.Id,
            PublisherName = $"sig-corrupt-{Guid.NewGuid():N}",
            Ed25519PublicKeyBase64 = pubB64,
        };
        db.TrustedPluginPublishers.Add(publisher);
        await db.SaveChangesAsync();

        var manifestJson = "{\"id\":\"acme-pacs\",\"version\":\"1.0.0\",\"capabilities\":[\"dicomweb.read\"]}";
        var canonical = PluginManifestSignatureVerifier.Canonicalize(manifestJson);
        var sig = Sign(canonical, priv);

        // Flip one byte of the signature.
        sig[0] ^= 0xFF;

        var publishers = await db.TrustedPluginPublishers
            .Where(p => p.TenantId == _factory.SeedTenant.Id)
            .ToListAsync();

        var before = await db.AuditEvents
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.ProviderBlocked)
            .CountAsync();

        var ex = await Assert.ThrowsAsync<PluginPolicyException>(() => verifier.VerifyAsync(
            _factory.SeedTenant.Id,
            _factory.SeedUser.Id,
            "acme-pacs",
            manifestJson,
            sig,
            publishers,
            CancellationToken.None));
        Assert.Equal("bad_signature", ex.Reason);

        var after = await db.AuditEvents
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.ProviderBlocked)
            .CountAsync();
        Assert.Equal(before + 1, after);

        var detail = await db.AuditEvents
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.ProviderBlocked)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.DetailsJson)
            .FirstAsync();
        Assert.Contains("plugin_policy", detail);
        Assert.Contains("bad_signature", detail);
        Assert.Contains("acme-pacs", detail);
    }

    [Fact]
    public async Task NoTrustedPublisher_Blocks()
    {
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var verifier = new PluginManifestSignatureVerifier(audit);

        var (priv, _) = NewKeyPair();
        var manifestJson = "{\"id\":\"x\",\"version\":\"0.0.1\"}";
        var sig = Sign(PluginManifestSignatureVerifier.Canonicalize(manifestJson), priv);

        var ex = await Assert.ThrowsAsync<PluginPolicyException>(() => verifier.VerifyAsync(
            _factory.SeedTenant.Id,
            _factory.SeedUser.Id,
            "x",
            manifestJson,
            sig,
            Array.Empty<TrustedPluginPublisher>(),
            CancellationToken.None));
        Assert.Equal("no_trusted_publisher", ex.Reason);
    }

    [Fact]
    public void Canonicalize_IsKeyOrderIndependent()
    {
        var a = PluginManifestSignatureVerifier.Canonicalize("{\"b\":1,\"a\":2}");
        var b = PluginManifestSignatureVerifier.Canonicalize("{\"a\":2,\"b\":1}");
        Assert.Equal(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }
}
