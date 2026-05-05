using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-31 INT-005 — verifies optional HMAC-SHA256 signature on FHIR
/// webhook calls. Bearer-only flow (no <c>FhirWebhookSecret</c> set) must
/// stay backward compatible. With a secret set, missing/wrong signature
/// returns 401 and audits a <c>fhir-webhook:bad_signature</c> policy
/// violation; correct signature returns 200.
/// </summary>
public class FhirWebhookSignatureTests : IClassFixture<RadioPadAppFactory>
{
    private const string Bearer = "fhir_sig_test_bearer";
    private const string Secret = "s3cr3t-shared-with-his";
    private readonly RadioPadAppFactory _factory;
    public FhirWebhookSignatureTests(RadioPadAppFactory f) => _factory = f;

    private async Task ConfigureAsync(bool withSignatureSecret)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
        s.IngestBearerSecret = Bearer;
        s.FhirWebhookSecret = withSignatureSecret ? Secret : "";
        await db.SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
    }

    private static string Sign(string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(mac).ToLowerInvariant();
    }

    private HttpRequestMessage Build(string body, string? signature)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/fhir/servicerequest")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/fhir+json"),
        };
        req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
        if (signature is not null) req.Headers.Add("X-RadioPad-Signature", signature);
        return req;
    }

    private static string MakeBody(string accession) => JsonSerializer.Serialize(new
    {
        resourceType = "ServiceRequest",
        id = "sr-sig-1",
        identifier = new[] { new { value = accession } },
        code = new { coding = new[] { new { display = "CT" } } },
        bodySite = new[] { new { text = "Chest" } },
    });

    [Fact]
    public async Task MissingSecret_BearerOnly_BackCompat()
    {
        await ConfigureAsync(withSignatureSecret: false);
        try
        {
            using var client = _factory.CreateClient();
            var body = MakeBody("ACC-SIG-BACK-1");
            var resp = await client.SendAsync(Build(body, signature: null));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally { await ResetAsync(); }
    }

    [Fact]
    public async Task ValidSignature_Returns_200()
    {
        await ConfigureAsync(withSignatureSecret: true);
        try
        {
            using var client = _factory.CreateClient();
            var body = MakeBody("ACC-SIG-OK-1");
            var resp = await client.SendAsync(Build(body, Sign(body)));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally { await ResetAsync(); }
    }

    [Fact]
    public async Task BadSignature_Returns_401_And_Audits()
    {
        await ConfigureAsync(withSignatureSecret: true);
        try
        {
            using var client = _factory.CreateClient();
            var body = MakeBody("ACC-SIG-BAD-1");
            var resp = await client.SendAsync(Build(body, "sha256=deadbeef"));
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var audited = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id
                && a.Action == AuditAction.PolicyViolation
                && a.DetailsJson.Contains("fhir-webhook:bad_signature"));
            Assert.True(audited);
        }
        finally { await ResetAsync(); }
    }
}
