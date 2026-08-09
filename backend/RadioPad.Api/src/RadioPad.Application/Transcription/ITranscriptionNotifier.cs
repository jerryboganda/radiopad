using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Reporting.Dtos;

namespace RadioPad.Application.Transcription;

public interface ITranscriptionNotifier
{
    Task NotifyTranscriptionCompletedAsync(DictationAudioDto dto, CancellationToken ct = default);
}
