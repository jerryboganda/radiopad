using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-32 DESK-007 / INT-007 — PACS bridge proxy.
///
/// All endpoints route through the tenant's configured DICOMweb base URL
/// (<see cref="TenantSettings.DicomWebBaseUrl"/>). The proxy stays
/// vendor-neutral: any DICOMweb-compliant target (Orthanc, DCM4CHEE, vendor)
/// works the same way. PHI minimisation: the proxy never logs accession
/// numbers or DICOM bodies — the audit row carries the upstream status code
/// and a SHA-256 hash of the accession only.
/// </summary>
[ApiController]
[Route("api/pacs")]
public class PacsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly Services.IDicomWebClient _dicom;
    private readonly IAuditLog _audit;

    public PacsController(RadioPadDbContext db, Services.IDicomWebClient dicom, IAuditLog audit)
    {
        _db = db;
        _dicom = dicom;
        _audit = audit;
    }

    /// <summary>
    /// QIDO-RS study search proxy. Forwards <c>?accession=...</c> as the
    /// vendor-neutral <c>AccessionNumber=</c> parameter. Returns
    /// <c>{ configured: false }</c> when DICOMweb is not configured.
    /// </summary>
    [HttpGet("studies")]
    public async Task<IActionResult> SearchStudies(
        [FromQuery] string? accession,
        [FromQuery] string? patientId,
        [FromQuery] string? studyDate,
        [FromQuery] string? modality,
        [FromQuery] int? limit,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user,
            UserRole.Radiologist, UserRole.MedicalDirector,
            UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.DicomWebBaseUrl))
            return Ok(new { configured = false, studies = Array.Empty<object>() });

        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(accession)) qs.Add($"AccessionNumber={Uri.EscapeDataString(accession)}");
        if (!string.IsNullOrWhiteSpace(patientId)) qs.Add($"PatientID={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(studyDate)) qs.Add($"StudyDate={Uri.EscapeDataString(studyDate)}");
        if (!string.IsNullOrWhiteSpace(modality)) qs.Add($"ModalitiesInStudy={Uri.EscapeDataString(modality)}");
        if (limit is int l && l > 0) qs.Add($"limit={Math.Min(l, 200)}");

        var (doc, statusCode) = await _dicom.SearchStudiesAsync(settings, string.Join('&', qs), ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.DicomContextFetched,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "qido",
                upstreamStatus = statusCode,
                accessionHash = string.IsNullOrWhiteSpace(accession)
                    ? null
                    : Sha256Short(accession),
            }),
        }, ct);

        if (doc is null)
            return Ok(new { configured = true, upstreamStatus = statusCode, studies = Array.Empty<object>() });

        // Upstream JSON is opaque DICOM JSON; pass through.
        var raw = System.Text.Json.JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
        doc.Dispose();
        return Ok(new { configured = true, upstreamStatus = statusCode, studies = raw });
    }

    /// <summary>
    /// Read-only health probe over the tenant's DICOMweb base URL plus an
    /// optional bundled-Orthanc check (<c>RADIOPAD_ORTHANC_URL</c>).
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct = default)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var dicomConfigured = settings is not null && !string.IsNullOrEmpty(settings.DicomWebBaseUrl);
        bool dicomReachable = false;
        if (dicomConfigured && settings is not null)
        {
            dicomReachable = await _dicom.HealthAsync(settings, ct);
        }

        bool orthancReachable = false;
        var orthancUrl = Environment.GetEnvironmentVariable("RADIOPAD_ORTHANC_URL");
        if (!string.IsNullOrWhiteSpace(orthancUrl))
        {
            orthancReachable = await ProbeOrthancAsync(orthancUrl!, ct);
        }

        return Ok(new
        {
            dicomWeb = new { configured = dicomConfigured, reachable = dicomReachable },
            orthanc = new
            {
                configured = !string.IsNullOrWhiteSpace(orthancUrl),
                reachable = orthancReachable,
                url = orthancUrl,
            },
        });
    }

    /// <summary>
    /// STOW-RS store proxy. Body must be DICOM bytes (multipart/related;
    /// type=application/dicom). Restricted to RT/IT roles.
    /// </summary>
    [HttpPost("studies")]
    [Microsoft.AspNetCore.Mvc.Consumes("multipart/related", "application/dicom", "application/octet-stream")]
    public async Task<IActionResult> StoreInstances(CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user,
            UserRole.Radiologist, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.DicomWebBaseUrl))
            return StatusCode(503, new { kind = "pacs_not_configured", error = "DICOMweb base URL not configured." });

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var body = ms.ToArray();
        var contentType = Request.ContentType ?? "multipart/related; type=\"application/dicom\"";

        var (statusCode, doc) = await _dicom.StoreInstancesAsync(settings, body, contentType, ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.DicomContextFetched,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "stow",
                upstreamStatus = statusCode,
                bytes = body.Length,
            }),
        }, ct);

        object? upstream = null;
        if (doc is not null)
        {
            upstream = System.Text.Json.JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            doc.Dispose();
        }
        if (statusCode == 0)
            return StatusCode(502, new { kind = "pacs_unreachable", error = "Upstream DICOMweb store failed." });
        return StatusCode(statusCode, new { upstreamStatus = statusCode, response = upstream });
    }

    private static async Task<bool> ProbeOrthancAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await client.GetAsync($"{baseUrl.TrimEnd('/')}/system", ct);
            return (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static string Sha256Short(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(b, 0, 6).ToLowerInvariant();
    }
}
