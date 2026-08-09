using System.Threading;
using System.Threading.Tasks;

namespace RadioPad.Application.Reporting.Services;

public class DictationSettingsService : IDictationSettingsService
{
    public const string DefaultActiveEngine = "medASR-6gram";
    public const string DefaultUbagModel = "ubag-gemini-audio";
    public const string DefaultRadiologySystemPrompt = "You are an expert medical radiology transcription assistant. The speaker is a senior radiologist dictating positive findings...";

    private readonly object _lock = new();
    private DictationSettingsDto _currentSettings = new(
        DefaultActiveEngine,
        DefaultUbagModel,
        DefaultRadiologySystemPrompt
    );

    public Task<DictationSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_currentSettings);
        }
    }

    public Task<DictationSettingsDto> UpdateSettingsAsync(DictationSettingsDto settings, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _currentSettings = settings with
            {
                ActiveEngine = string.IsNullOrWhiteSpace(settings.ActiveEngine) ? DefaultActiveEngine : settings.ActiveEngine,
                UbagModel = string.IsNullOrWhiteSpace(settings.UbagModel) ? DefaultUbagModel : settings.UbagModel,
                RadiologySystemPrompt = string.IsNullOrWhiteSpace(settings.RadiologySystemPrompt) ? DefaultRadiologySystemPrompt : settings.RadiologySystemPrompt
            };
            return Task.FromResult(_currentSettings);
        }
    }
}
