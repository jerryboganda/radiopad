using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Auth;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 AUTH-006 — sliding-window account lockout. Five failed TOTP
/// codes within 15 minutes lock the account; an admin /unlock clears the
/// lock and the failure window. /revoke-sessions bumps SessionEpoch and
/// audits as <see cref="AuditAction.SessionsRevoked"/>.
/// </summary>
public class Iter32AccountLockoutTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32AccountLockoutTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task FiveBadTotpAttempts_LockTheAccount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var lockout = scope.ServiceProvider.GetRequiredService<LockoutPolicy>();

        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"lock-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Lock Test",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        for (var i = 0; i < LockoutPolicy.MaxAttempts; i++)
        {
            await lockout.OnFailureAsync(user, "totp", default);
        }

        Assert.True(LockoutPolicy.IsLocked(user));
        Assert.Equal(LockoutPolicy.MaxAttempts, user.FailedLoginCount);
        Assert.NotNull(user.LockedUntil);

        var alerts = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id
                     && e.UserId == user.Id
                     && e.Action == AuditAction.UserLockedOut)
            .CountAsync();
        Assert.True(alerts >= 1);
    }

    [Fact]
    public async Task SuccessAfterFailures_ClearsCounter()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var lockout = scope.ServiceProvider.GetRequiredService<LockoutPolicy>();

        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"clear-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Clear Test",
            Role = UserRole.Radiologist,
            FailedLoginCount = 3,
            FailedLoginWindowStart = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await lockout.OnSuccessAsync(user, default);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.FailedLoginWindowStart);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task RevokeSessionsEndpoint_BumpsEpoch_AndAuditsSessionsRevoked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var admin = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"itadmin-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Admin",
            Role = UserRole.ItAdmin,
        };
        var target = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"target-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Target",
            Role = UserRole.Radiologist,
            SessionEpoch = 0,
        };
        db.Users.AddRange(admin, target);
        await db.SaveChangesAsync();

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", admin.Email);

        var resp = await http.PostAsync($"/api/users/{target.Id}/revoke-sessions", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        Assert.Equal(1, refreshed.SessionEpoch);

        var revokedRows = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id
                     && e.Action == AuditAction.SessionsRevoked)
            .CountAsync();
        Assert.True(revokedRows >= 1);
    }

    [Fact]
    public async Task UnlockEndpoint_ClearsLockAndCounter()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var admin = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"itadmin-unlock-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Admin",
            Role = UserRole.ItAdmin,
        };
        var target = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"locked-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Locked",
            Role = UserRole.Radiologist,
            FailedLoginCount = 5,
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15),
            IsActive = false,
        };
        db.Users.AddRange(admin, target);
        await db.SaveChangesAsync();

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", admin.Email);

        var resp = await http.PostAsync($"/api/users/{target.Id}/unlock", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        Assert.True(refreshed.IsActive);
        Assert.Null(refreshed.LockedUntil);
        Assert.Equal(0, refreshed.FailedLoginCount);
    }
}

/// <summary>
/// Iter-32 AUTH-001 — OIDC preset registration. Verifies that
/// <see cref="OidcProfiles"/> emits the documented set and that
/// <see cref="OidcProfiles.ApplyToEnvironment"/> populates env vars
/// without overwriting operator-supplied overrides.
/// </summary>
public class Iter32OidcPresetTests
{
    [Fact]
    public void Resolve_ReturnsKnownPresets()
    {
        Assert.NotNull(OidcProfiles.Resolve("keycloak"));
        Assert.NotNull(OidcProfiles.Resolve("auth0"));
        Assert.NotNull(OidcProfiles.Resolve("OKTA"));
        Assert.Null(OidcProfiles.Resolve("does-not-exist"));
    }

    [Fact]
    public void Apply_FillsDefaults_AndPreservesOperatorOverride()
    {
        var prevTenant = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM");
        var prevEmail = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM");
        var prevMfa = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM", "custom_tenant");
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA", null);

            var profile = OidcProfiles.ApplyToEnvironment("keycloak");
            Assert.NotNull(profile);
            Assert.Equal("custom_tenant", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM"));
            Assert.Equal("email", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM"));
            Assert.Equal("1", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM", prevTenant);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM", prevEmail);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA", prevMfa);
        }
    }
}

/// <summary>
/// Iter-32 INT-002 — SAML 2.0 ACS happy-path / failure path. Builds a
/// minimal unsigned SAML response and verifies the controller rejects it
/// when no IdP cert is configured and the assertion is invalid; the
/// metadata endpoint emits well-formed XML containing the SP entity id.
/// </summary>
[Collection(EnvironmentVariableCollection.Name)]
public class Iter32SamlAcsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32SamlAcsTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Metadata_EmitsSpDescriptor()
    {
        var http = _factory.CreateClient();
        var resp = await http.GetAsync("/saml/metadata");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("EntityDescriptor", body);
        Assert.Contains("AssertionConsumerService", body);
    }

    [Fact]
    public async Task Acs_RejectsMissingResponse()
    {
        var http = _factory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>());
        var resp = await http.PostAsync("/saml/acs", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Acs_HappyPath_UnsignedAssertion_When_NoIdpCertConfigured()
    {
        // With no RADIOPAD_SAML_IDP_CERT_PEM env var, the controller is
        // fail-CLOSED by default (iter-32 closeout, Momus finding #1). The
        // explicit dev escape hatch RADIOPAD_SAML_DEV_INSECURE=true is the
        // ONLY way to accept an unsigned assertion in tests.
        var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
        var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
        var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var samlUser = new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = $"saml-{Guid.NewGuid():N}@radiopad.local",
                DisplayName = "SAML User",
                Role = UserRole.Radiologist,
            };
            db.Users.Add(samlUser);
            await db.SaveChangesAsync();

            var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
  <saml:Assertion>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{samlUser.Email}</saml:NameID>
    </saml:Subject>
    <saml:AttributeStatement>
      <saml:Attribute Name=""tenant_slug"">
        <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
      </saml:Attribute>
    </saml:AttributeStatement>
  </saml:Assertion>
</samlp:Response>";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
            var http = _factory.CreateClient();
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("SAMLResponse", b64),
            });
            var resp = await http.PostAsync("/saml/acs", form);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(_factory.SeedTenant.Slug, body.GetProperty("tenant").GetString());
            Assert.Equal(samlUser.Email, body.GetProperty("user").GetString());
            Assert.StartsWith("rp_", body.GetProperty("token").GetString());

            var login = await db.AuditEvents.AsNoTracking()
                .Where(e => e.TenantId == _factory.SeedTenant.Id
                         && e.UserId == samlUser.Id
                         && e.Action == AuditAction.UserLogin)
                .ToListAsync();
            Assert.Contains(login, e => e.DetailsJson.Contains("\"saml\""));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
        }
    }

    [Fact]
    public async Task Acs_FailClosed_When_NoCert_And_No_DevInsecureFlag()
    {
        // Iter-32 closeout regression test: with neither
        // RADIOPAD_SAML_IDP_CERT_PEM nor RADIOPAD_SAML_DEV_INSECURE set,
        // an otherwise-valid unsigned assertion MUST be rejected. Prevents
        // the fail-open auth bypass flagged by Momus review #1.
        var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
        var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
        var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
  <saml:Assertion>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">attacker@example.com</saml:NameID>
    </saml:Subject>
    <saml:AttributeStatement>
      <saml:Attribute Name=""tenant_slug"">
        <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
      </saml:Attribute>
    </saml:AttributeStatement>
  </saml:Assertion>
</samlp:Response>";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
            var http = _factory.CreateClient();
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("SAMLResponse", b64),
            });
            var resp = await http.PostAsync("/saml/acs", form);
            // Controller returns 401 Unauthorized when ProcessAcs returns null.
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
                }
        }

        [Theory]
        [InlineData("ASPNETCORE_ENVIRONMENT")]
        [InlineData("DOTNET_ENVIRONMENT")]
        public async Task Acs_FailClosed_InProduction_EvenWithDevInsecureFlag(string environmentVariableName)
        {
                var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
                var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
            var prevAspNetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var prevDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                try
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", "true");
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
                Environment.SetEnvironmentVariable(environmentVariableName, "Production");

                        var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
    <saml:Assertion>
        <saml:Subject>
            <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">attacker@example.com</saml:NameID>
        </saml:Subject>
        <saml:AttributeStatement>
            <saml:Attribute Name=""tenant_slug"">
                <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
            </saml:Attribute>
        </saml:AttributeStatement>
    </saml:Assertion>
</samlp:Response>";
                        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
                        var http = _factory.CreateClient();
                        var form = new FormUrlEncodedContent(new[]
                        {
                                new KeyValuePair<string, string>("SAMLResponse", b64),
                        });
                        var resp = await http.PostAsync("/saml/acs", form);

                        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                }
                finally
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevAspNetEnvironment);
                        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prevDotnetEnvironment);
        }
    }

        [Fact]
        public async Task Acs_RejectsSignedAssertion_WhenSignatureReferenceDoesNotCoverAssertion()
        {
                var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
                var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
                var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                try
                {
                        using var rsa = RSA.Create(2048);
                        var request = new CertificateRequest("CN=RadioPad Test SAML IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", ExportCertificatePem(cert));
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", null);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

                        var assertion = BuildSignedSamlResponse(
                                _factory.SeedUser.Email,
                                _factory.SeedTenant.Slug,
                                cert,
                                "#_signed-conditions");
                        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(assertion));
                        var http = _factory.CreateClient();
                        var form = new FormUrlEncodedContent(new[]
                        {
                                new KeyValuePair<string, string>("SAMLResponse", b64),
                        });

                        var resp = await http.PostAsync("/saml/acs", form);

                        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                }
                finally
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
                }
        }

        private static string BuildSignedSamlResponse(string email, string tenantSlug, X509Certificate2 cert, string referenceUri)
        {
                var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
                var notOnOrAfter = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
                var xml = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
    <saml:Assertion ID=""_assertion"">
        <saml:Issuer>test-idp</saml:Issuer>
        <saml:Subject>
            <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{email}</saml:NameID>
        </saml:Subject>
        <saml:Conditions ID=""_signed-conditions"" NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}"">
            <saml:AudienceRestriction>
                <saml:Audience>https://radiopad.local/saml</saml:Audience>
            </saml:AudienceRestriction>
        </saml:Conditions>
        <saml:AttributeStatement>
            <saml:Attribute Name=""tenant_slug"">
                <saml:AttributeValue>{tenantSlug}</saml:AttributeValue>
            </saml:Attribute>
        </saml:AttributeStatement>
    </saml:Assertion>
</samlp:Response>";

                var doc = new XmlDocument { PreserveWhitespace = true };
                doc.LoadXml(xml);
                var assertion = (XmlElement)doc.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
                var signed = new SignedXml(assertion)
                {
                        SigningKey = cert.GetRSAPrivateKey(),
                };
                signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
                var reference = new Reference(referenceUri);
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigExcC14NTransform());
                signed.AddReference(reference);
                var keyInfo = new KeyInfo();
                keyInfo.AddClause(new KeyInfoX509Data(cert));
                signed.KeyInfo = keyInfo;
                signed.ComputeSignature();

                var importedSignature = doc.ImportNode(signed.GetXml(), true);
                var issuer = assertion.GetElementsByTagName("Issuer", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
                assertion.InsertAfter(importedSignature, issuer);
                return doc.OuterXml;
        }

        private static string ExportCertificatePem(X509Certificate2 cert) =>
                PemEncoding.WriteString("CERTIFICATE", cert.Export(X509ContentType.Cert));
}

/// <summary>
/// Iter-32 AUTH-001 — WebAuthn registration + signin happy paths.
/// The integration deliberately does not exercise full attestation
/// verification (deferred to a follow-up); it asserts that the option
/// envelopes are well-formed and that registering then listing surfaces
/// the credential to the operator.
/// </summary>
public class Iter32WebAuthnFlowTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32WebAuthnFlowTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task RegisterOptions_ReturnsChallengeAndRpId()
    {
        var http = _factory.CreateTenantClient();
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register-options", new { label = "yubikey" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("challenge").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("rp").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Register_ThenList_ShowsCredential()
    {
        var http = _factory.CreateTenantClient();
        var (attObj, clientData) = RadioPad.Api.Tests.Iter33.WebAuthnTestVectors.NoneAttestation();
        var register = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "Integration",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var list = await http.GetFromJsonAsync<List<JsonElement>>("/api/auth/webauthn/credentials");
        Assert.NotNull(list);
        Assert.Contains(list!, c => c.GetProperty("label").GetString() == "Integration");
    }

    [Fact]
    public async Task SignIn_WithUnknownCredential_FailsAndAccrues()
    {
        var http = _factory.CreateTenantClient();
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = "definitely-not-registered",
            clientDataJson = "{}",
            authenticatorData = "AA==",
            signature = "AA==",
            signCount = 1u,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

internal static class JsonElementHelpers
{
}
