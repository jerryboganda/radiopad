using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RadioPad.Application.Reporting.Services;

public record DictationSettingsDto(
    [property: JsonPropertyName("activeEngine")] string ActiveEngine,
    [property: JsonPropertyName("ubagModel")] string UbagModel,
    [property: JsonPropertyName("radiologySystemPrompt")] string RadiologySystemPrompt
);

public interface IDictationSettingsService
{
    Task<DictationSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<DictationSettingsDto> UpdateSettingsAsync(DictationSettingsDto settings, CancellationToken ct = default);
}
