using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Transcription;

namespace RadioPad.Api.Hubs;

public class SignalRTranscriptionNotifier : ITranscriptionNotifier
{
    private readonly IHubContext<ReportingHub> _hubContext;

    public SignalRTranscriptionNotifier(IHubContext<ReportingHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyTranscriptionCompletedAsync(DictationAudioDto dto, CancellationToken ct = default)
    {
        await _hubContext.Clients.Group($"report-{dto.ReportId}")
            .SendAsync("TranscriptionCompleted", dto, cancellationToken: ct);
        await _hubContext.Clients.All
            .SendAsync("TranscriptionCompleted", dto, cancellationToken: ct);
    }
}
