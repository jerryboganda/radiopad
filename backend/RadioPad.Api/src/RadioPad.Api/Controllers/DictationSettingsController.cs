using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Reporting.Services;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Admin panel dictation settings controller.
/// Manages active transcription engine selection (medASR 6-gram default),
/// UBAG audio AI model configuration, and radiology system prompt settings.
/// </summary>
[ApiController]
[Route("api/v1/admin/dictation-settings")]
[AllowAnonymous]
public class DictationSettingsController : ControllerBase
{
    private readonly IDictationSettingsService _settingsService;

    public DictationSettingsController(IDictationSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    public async Task<ActionResult<DictationSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPost]
    public async Task<ActionResult<DictationSettingsDto>> UpdateSettings([FromBody] DictationSettingsDto settings, CancellationToken ct)
    {
        if (settings is null)
        {
            return BadRequest(new { error = "Settings payload is required." });
        }

        var updated = await _settingsService.UpdateSettingsAsync(settings, ct);
        return Ok(updated);
    }
}
