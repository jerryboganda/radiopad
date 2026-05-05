using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-001 / INT-002 — SAML 2.0 service-provider endpoints. Two routes:
///
/// <list type="bullet">
/// <item><c>GET /saml/metadata</c> — emits the SP metadata XML so an IdP
/// admin can register RadioPad without copy-pasting URLs.</item>
/// <item><c>POST /saml/acs</c> — Assertion Consumer Service. Validates the
/// XML signature on the SAMLResponse, extracts <c>NameID</c> and a tenant
/// attribute, then mints the same kind of bearer token used by the
/// magic-link flow so downstream controllers see the existing
/// <c>X-RadioPad-Tenant</c> / <c>X-RadioPad-User</c> projection.</item>
/// </list>
///
/// Configuration (env-only; never committed):
/// <list type="bullet">
///   <item><c>RADIOPAD_SAML_ENTITY_ID</c> — SP entity id; default <c>https://radiopad.local/saml</c>.</item>
///   <item><c>RADIOPAD_SAML_ACS_URL</c> — public ACS URL advertised in metadata.</item>
///   <item><c>RADIOPAD_SAML_IDP_CERT_PEM</c> — IdP signing certificate (PEM, single line or env-encoded).</item>
///   <item><c>RADIOPAD_SAML_TENANT_ATTRIBUTE</c> — name of the Attribute carrying the tenant slug; default <c>tenant_slug</c>.</item>
/// </list>
///
/// The implementation is intentionally lightweight (no Sustainsys.Saml2
/// AspNetCore middleware) so it does not collide with the existing dev
/// header pipeline. <see cref="ProcessAcs"/> verifies the XML digital
/// signature against the configured IdP certificate and rejects the
/// assertion on any mismatch. When no IdP certificate is configured
/// the controller is fail-CLOSED — operators must opt in to insecure
/// dev mode by setting <c>RADIOPAD_SAML_DEV_INSECURE=true</c>.
/// </summary>
[ApiController]
[Route("saml")]
public class SamlController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ILogger<SamlController> _log;

    public SamlController(RadioPadDbContext db, IAuditLog audit, ILogger<SamlController> log)
    { _db = db; _audit = audit; _log = log; }

    [HttpGet("metadata")]
    [Produces("application/xml")]
    public IActionResult Metadata()
    {
        var entityId = Environment.GetEnvironmentVariable("RADIOPAD_SAML_ENTITY_ID")
            ?? "https://radiopad.local/saml";
        var acsUrl = Environment.GetEnvironmentVariable("RADIOPAD_SAML_ACS_URL")
            ?? $"{Request.Scheme}://{Request.Host.Value}/saml/acs";
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<EntityDescriptor xmlns=""urn:oasis:names:tc:SAML:2.0:metadata"" entityID=""{System.Security.SecurityElement.Escape(entityId)}"">
  <SPSSODescriptor protocolSupportEnumeration=""urn:oasis:names:tc:SAML:2.0:protocol"" AuthnRequestsSigned=""false"" WantAssertionsSigned=""true"">
    <NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</NameIDFormat>
    <AssertionConsumerService index=""0"" isDefault=""true""
        Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST""
        Location=""{System.Security.SecurityElement.Escape(acsUrl)}"" />
  </SPSSODescriptor>
</EntityDescriptor>";
        return Content(xml, "application/xml");
    }

    public record AcsForm(string SAMLResponse, string? RelayState);

    [HttpPost("acs")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> Acs([FromForm] AcsForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form?.SAMLResponse))
            return BadRequest(new { error = "Missing SAMLResponse.", kind = "validation" });

        try
        {
            var xmlBytes = Convert.FromBase64String(form.SAMLResponse);
            var xml = Encoding.UTF8.GetString(xmlBytes);
            var parsed = ProcessAcs(xml);
            if (parsed is null)
                return Unauthorized(new { error = "SAML signature invalid.", kind = "unauthenticated" });

            var (tenantSlug, email) = parsed.Value;
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantSlug, ct);
            if (tenant is null) return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });
            var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct);
            if (user is null || !user.IsActive)
                return Unauthorized(new { error = "Unknown user.", kind = "unauthenticated" });
            if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
                return Unauthorized(new { error = "Account locked.", kind = "unauthenticated" });

            var token = MagicLinkController.MintBearer(tenant.Slug, user.Email, user.SessionEpoch);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.UserLogin,
                DetailsJson = JsonSerializer.Serialize(new { method = "saml" }),
            }, ct);

            return Ok(new
            {
                token,
                tenant = tenant.Slug,
                user = user.Email,
                expiresAt = DateTimeOffset.UtcNow.AddHours(12),
                relayState = form.RelayState,
            });
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "SAMLResponse is not valid base64.", kind = "validation" });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SAML ACS rejected.");
            return Unauthorized(new { error = "SAML assertion rejected.", kind = "unauthenticated" });
        }
    }

    /// <summary>
    /// Verify the XML signature on a SAMLResponse and extract the NameID
    /// (as the email) and the tenant slug attribute. Returns null if the
    /// signature does not validate or required fields are missing.
    /// </summary>
    public static (string TenantSlug, string Email)? ProcessAcs(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        nsm.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        nsm.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var certPem = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
        if (string.IsNullOrWhiteSpace(certPem))
        {
            // SECURITY (iter-32 closeout, Momus finding #1): SAML ACS is
            // fail-CLOSED when no IdP cert is configured. An explicit dev
            // escape hatch (`RADIOPAD_SAML_DEV_INSECURE=true`) lets local
            // integration tests run without a real IdP, but the default
            // posture for any real environment is to refuse the assertion.
            var devInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
            if (!string.Equals(devInsecure, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        else
        {
            // Verify the assertion signature against the configured IdP cert.
            var cert = LoadCert(certPem);
            var signedNode = doc.SelectSingleNode("//ds:Signature", nsm) as XmlElement;
            if (signedNode is null) return null;
            var signed = new SignedXml((XmlElement)signedNode.ParentNode!);
            signed.LoadXml(signedNode);
            if (!signed.CheckSignature(cert, true)) return null;
        }

        var nameId = doc.SelectSingleNode("//saml:Subject/saml:NameID", nsm)?.InnerText;
        var attrName = Environment.GetEnvironmentVariable("RADIOPAD_SAML_TENANT_ATTRIBUTE") ?? "tenant_slug";
        var tenantValue = doc.SelectSingleNode(
            $"//saml:Attribute[@Name='{attrName}']/saml:AttributeValue", nsm)?.InnerText;
        if (string.IsNullOrWhiteSpace(nameId) || string.IsNullOrWhiteSpace(tenantValue)) return null;
        return (tenantValue.Trim(), nameId.Trim());
    }

    private static X509Certificate2 LoadCert(string pem)
    {
        var clean = pem.Replace("\\n", "\n").Trim();
        if (!clean.Contains("-----BEGIN"))
        {
            // Plain base64 body — wrap.
            clean = "-----BEGIN CERTIFICATE-----\n" + clean + "\n-----END CERTIFICATE-----";
        }
        return X509Certificate2.CreateFromPem(clean);
    }
}
