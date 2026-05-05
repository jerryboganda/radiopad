using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Services.Siem;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-32 INT-010 — surfaces the in-process status of the SIEM push
/// service to <c>/admin/security</c>. Read-only; the worker itself is
/// configured by env vars (see <see cref="SplunkHecSink"/>,
/// <see cref="SentinelLogAnalyticsSink"/>, <see cref="ElasticBulkSink"/>,
/// <see cref="SyslogUdpSink"/>).
/// </summary>
[ApiController]
[Route("api/siem")]
public class SiemController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IEnumerable<ISiemSink> _sinks;
    private readonly SiemStatusRegistry _status;

    public SiemController(RadioPadDbContext db, IEnumerable<ISiemSink> sinks, SiemStatusRegistry status)
    {
        _db = db;
        _sinks = sinks;
        _status = status;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.MedicalDirector, UserRole.ComplianceReviewer);
        if (deny is not null) return deny;

        var snapshot = _status.Snapshot();
        var sinks = _sinks.Select(s => new
        {
            name = s.Name,
            configured = s.Configured,
            lastPushAt = snapshot.TryGetValue(s.Name, out var st) ? st.LastPushAt : null,
            lastError = snapshot.TryGetValue(s.Name, out var st2) ? st2.LastError : null,
            totalPushed = snapshot.TryGetValue(s.Name, out var st3) ? st3.TotalPushed : 0,
            totalErrors = snapshot.TryGetValue(s.Name, out var st4) ? st4.TotalErrors : 0,
        }).ToArray();
        return Ok(new { sinks });
    }
}
