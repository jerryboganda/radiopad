using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Transcription;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Hubs;

public class SignalRTranscriptionNotifier : ITranscriptionNotifier
{
    private readonly IHubContext<ReportingHub> _hubContext;
    private readonly RadioPadDbContext _db;
    private readonly IAiJobEventBus _bus;

    public SignalRTranscriptionNotifier(IHubContext<ReportingHub> hubContext, RadioPadDbContext db, IAiJobEventBus bus)
    {
        _hubContext = hubContext;
        _db = db;
        _bus = bus;
    }

    public async Task NotifyTranscriptionCompletedAsync(DictationAudioDto dto, CancellationToken ct = default)
    {
        // Live push over the SSE stream the desktop already holds open. The tenant comes from the
        // report row (never the caller), and the event is a pure signal — no transcript, no storage
        // path: the desktop refetches through the authenticated GET.
        var tenantId = await _db.Reports.AsNoTracking()
            .Where(r => r.Id == dto.ReportId)
            .Select(r => (Guid?)r.TenantId)
            .FirstOrDefaultAsync(ct);
        if (tenantId is Guid tid)
        {
            _bus.PublishTenantEvent(tid, "dictation", new
            {
                kind = "transcribed",
                reportId = dto.ReportId,
                dictationId = dto.Id,
                status = dto.Status,
                transcriptionEngine = dto.TranscriptionEngine,
                durationSeconds = dto.DurationSeconds,
                uploadedAt = dto.UploadedAt,
                transcribedAt = dto.TranscribedAt,
            });
        }

        // Report-scoped only. The payload carries the transcript (PHI): a broadcast to every
        // connected client would hand one tenant's dictation to any other connection.
        await _hubContext.Clients.Group($"report-{dto.ReportId}")
            .SendAsync("TranscriptionCompleted", dto, cancellationToken: ct);
    }
}
