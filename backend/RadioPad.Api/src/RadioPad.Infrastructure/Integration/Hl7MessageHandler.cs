using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Integration;

/// <summary>
/// Iter-31 INT-006 — converts a parsed HL7 v2 ORU^R01 / ORM^O01 message into
/// a Draft <see cref="Report"/> for the matching tenant. Idempotent on
/// accession number (re-delivery → AA + <c>deduplicated:true</c>). Audits
/// <see cref="AuditAction.OrderIngested"/> for both new and deduped paths so
/// the support team has a record that the sender retried.
/// </summary>
public sealed class Hl7MessageHandler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<Hl7MessageHandler> _log;

    public Hl7MessageHandler(IServiceScopeFactory scopes, ILogger<Hl7MessageHandler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public sealed record HandleResult(string Ack, bool Accepted, bool Deduplicated, Guid? ReportId);

    public async Task<HandleResult> HandleAsync(string hl7, CancellationToken ct)
    {
        Hl7Parser.ParsedHl7 parsed;
        try
        {
            parsed = Hl7Parser.Parse(hl7);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HL7 parse error");
            // Synthesize a minimal AR ack since we couldn't parse the inbound MSH.
            var stub = new Hl7Parser.ParsedHl7(
                MessageType: "", SendingApplication: "", SendingFacility: "",
                MessageControlId: "", ProcessingId: "P", VersionId: "2.5",
                Accession: "", Modality: "", Indication: "", PatientReference: "",
                FieldSep: '|', ComponentSep: '^', RepetitionSep: '~',
                EscapeChar: '\\', SubcomponentSep: '&');
            return new HandleResult(Hl7Parser.BuildAck(stub, "AR", "Parse error"), false, false, null);
        }

        var typeRoot = parsed.MessageType.Split('^')[0];
        if (typeRoot is not ("ORU" or "ORM"))
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AR", $"Unsupported message type {parsed.MessageType}"), false, false, null);

        if (string.IsNullOrEmpty(parsed.SendingFacility))
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AR", "MSH-4 sending facility required"), false, false, null);
        if (string.IsNullOrEmpty(parsed.Accession))
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AE", "OBR-3 accession required"), false, false, null);
        if (string.IsNullOrEmpty(parsed.Modality))
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AE", "OBR-21/OBR-4 modality required"), false, false, null);

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        // Tenant resolution by sending facility (case-insensitive).
        var lower = parsed.SendingFacility.ToLowerInvariant();
        var settings = await db.TenantSettings
            .FirstOrDefaultAsync(s => s.Hl7SendingFacility != "" && s.Hl7SendingFacility.ToLower() == lower, ct);
        if (settings is null)
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AR", "Unknown sending facility"), false, false, null);

        var tenantId = settings.TenantId;

        var existing = await db.Reports.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Study.AccessionNumber == parsed.Accession, ct);
        if (existing is not null)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenantId,
                ReportId = existing.Id,
                Action = AuditAction.OrderIngested,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    source = "hl7-mllp",
                    sendingFacility = parsed.SendingFacility,
                    messageType = parsed.MessageType,
                    accession = parsed.Accession,
                    deduplicated = true,
                }),
            }, ct);
            return new HandleResult(Hl7Parser.BuildAck(parsed, "AA"), true, true, existing.Id);
        }

        var report = new Report
        {
            TenantId = tenantId,
            Status = ReportStatus.Draft,
            Indication = parsed.Indication,
            Comparison = "",
            Study = new StudyContext
            {
                AccessionNumber = parsed.Accession,
                Modality = parsed.Modality,
                BodyPart = "",
                // Iter-36 — study-context Indication removed; the report-body
                // Indication section (set above) carries the clinical indication.
                PatientReference = parsed.PatientReference,
            },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            ReportId = report.Id,
            Action = AuditAction.OrderIngested,
            DetailsJson = JsonSerializer.Serialize(new
            {
                source = "hl7-mllp",
                sendingFacility = parsed.SendingFacility,
                messageType = parsed.MessageType,
                accession = parsed.Accession,
                modality = parsed.Modality,
                deduplicated = false,
            }),
        }, ct);
        return new HandleResult(Hl7Parser.BuildAck(parsed, "AA"), true, false, report.Id);
    }
}
