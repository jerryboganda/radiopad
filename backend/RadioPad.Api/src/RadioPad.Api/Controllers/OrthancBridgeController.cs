using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Hl7Bridge;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-33 INT-008 — bearer-protected ingress for the Orthanc Lua bridge.
///
/// <list type="bullet">
///   <item><c>POST /api/integrations/orthanc/study-stable</c> — Orthanc fires
///   this when <c>OnStableStudy</c> reports a stable study. RadioPad records
///   an <c>AuditAction.StudyReceived</c> entry on the matching tenant.</item>
///   <item><c>POST /api/integrations/orthanc/sr-stored</c> — Orthanc fires
///   this when a Modality=SR instance lands in storage. The body is a DICOM
///   SR JSON-tag payload; the controller runs <see cref="DicomSrToHl7Converter"/>
///   and enqueues the resulting ORU^R01 to the in-process HL7 outbox.</item>
/// </list>
///
/// Auth uses a single shared bearer (<c>RADIOPAD_BRIDGE_TOKEN</c>) compared
/// in constant time. The tenant slug for both hooks is configured at deploy
/// time via <c>RADIOPAD_BRIDGE_TENANT</c> (default <c>dev</c>); when the slug
/// is unknown the controller returns 503 so misconfiguration surfaces loudly
/// rather than silently dropping audit rows.
/// </summary>
[ApiController]
[Route("api/integrations/orthanc")]
public class OrthancBridgeController : ControllerBase
{
    public const string TokenEnvVar = "RADIOPAD_BRIDGE_TOKEN";
    public const string TenantEnvVar = "RADIOPAD_BRIDGE_TENANT";

    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IHl7Outbox _outbox;

    public OrthancBridgeController(RadioPadDbContext db, IAuditLog audit, IHl7Outbox outbox)
    {
        _db = db;
        _audit = audit;
        _outbox = outbox;
    }

    public sealed record StudyStableDto(
        string? PatientId,
        string? AccessionNumber,
        string? StudyInstanceUid,
        string? Modality,
        string? StudyDate);

    [HttpPost("study-stable")]
    public async Task<IActionResult> StudyStable([FromBody] StudyStableDto dto, CancellationToken ct)
    {
        if (!CheckBearer()) return Unauthorized(new { error = "invalid_bearer", kind = "auth" });
        var tenant = await ResolveTenantAsync(ct);
        if (tenant is null) return ServiceUnavailable("bridge_tenant_unknown");

        var details = JsonSerializer.Serialize(new
        {
            source = "orthanc-lua",
            patientReference = dto.PatientId ?? "",
            accession = dto.AccessionNumber ?? "",
            studyInstanceUid = dto.StudyInstanceUid ?? "",
            modality = dto.Modality ?? "",
            studyDate = dto.StudyDate ?? "",
        });
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            Action = AuditAction.StudyReceived,
            DetailsJson = details,
        }, ct);

        return Ok(new { accepted = true });
    }

    [HttpPost("sr-stored")]
    public async Task<IActionResult> SrStored([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!CheckBearer()) return Unauthorized(new { error = "invalid_bearer", kind = "auth" });
        var tenant = await ResolveTenantAsync(ct);
        if (tenant is null) return ServiceUnavailable("bridge_tenant_unknown");

        // Adapt the inbound JsonElement → JsonObject for the converter.
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "expected DICOM SR JSON object.", kind = "validation" });
        var sr = JsonNode.Parse(body.GetRawText()) as JsonObject;
        if (sr is null)
            return BadRequest(new { error = "could not parse SR JSON.", kind = "validation" });

        Hl7Message hl7;
        try
        {
            hl7 = DicomSrToHl7Converter.Convert(sr);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, kind = "validation" });
        }

        if (string.IsNullOrEmpty(hl7.AccessionNumber))
            return BadRequest(new { error = "DICOM SR missing 00080050 AccessionNumber.", kind = "validation" });

        var serialized = hl7.Serialize();
        _outbox.Enqueue(tenant.Id, hl7.AccessionNumber, serialized);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            Action = AuditAction.OrderIngested,
            DetailsJson = JsonSerializer.Serialize(new
            {
                source = "orthanc-sr",
                accession = hl7.AccessionNumber,
                obxCount = hl7.Observations.Count,
            }),
        }, ct);

        return Ok(new { accepted = true, accession = hl7.AccessionNumber, obxCount = hl7.Observations.Count });
    }

    private bool CheckBearer()
    {
        var expected = Environment.GetEnvironmentVariable(TokenEnvVar) ?? string.Empty;
        if (string.IsNullOrEmpty(expected)) return false;

        var header = Request.Headers["Authorization"].FirstOrDefault() ?? string.Empty;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = header.Substring(prefix.Length);

        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private async Task<Tenant?> ResolveTenantAsync(CancellationToken ct)
    {
        var slug = Environment.GetEnvironmentVariable(TenantEnvVar);
        if (string.IsNullOrWhiteSpace(slug)) slug = "dev";
        return await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
    }

    private IActionResult ServiceUnavailable(string kind)
        => new ObjectResult(new { error = kind, kind = "configuration" })
        { StatusCode = StatusCodes.Status503ServiceUnavailable };
}
