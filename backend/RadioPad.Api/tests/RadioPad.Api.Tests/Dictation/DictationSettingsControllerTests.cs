using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Controllers;
using RadioPad.Application.Reporting.Services;
using Xunit;

namespace RadioPad.Api.Tests.Dictation;

public class DictationSettingsControllerTests
{
    [Fact]
    public async Task GetSettings_ReturnsDefaultSettings()
    {
        var service = new DictationSettingsService();
        var controller = new DictationSettingsController(service);

        var actionResult = await controller.GetSettings(default);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var settings = Assert.IsType<DictationSettingsDto>(okResult.Value);

        Assert.Equal("medASR-6gram", settings.ActiveEngine);
        Assert.Equal("ubag-gemini-audio", settings.UbagModel);
        Assert.Contains("You are an expert medical radiology transcription assistant", settings.RadiologySystemPrompt);
    }

    [Fact]
    public async Task UpdateSettings_SavesAndReturnsUpdatedSettings()
    {
        var service = new DictationSettingsService();
        var controller = new DictationSettingsController(service);

        var newSettings = new DictationSettingsDto(
            ActiveEngine: "ubag-cloud",
            UbagModel: "ubag-chatgpt-audio",
            RadiologySystemPrompt: "Updated radiology prompt for testing"
        );

        var postResult = await controller.UpdateSettings(newSettings, default);
        var okPost = Assert.IsType<OkObjectResult>(postResult.Result);
        var updated = Assert.IsType<DictationSettingsDto>(okPost.Value);

        Assert.Equal("ubag-cloud", updated.ActiveEngine);
        Assert.Equal("ubag-chatgpt-audio", updated.UbagModel);
        Assert.Equal("Updated radiology prompt for testing", updated.RadiologySystemPrompt);

        var getResult = await controller.GetSettings(default);
        var okGet = Assert.IsType<OkObjectResult>(getResult.Result);
        var fetched = Assert.IsType<DictationSettingsDto>(okGet.Value);

        Assert.Equal("ubag-cloud", fetched.ActiveEngine);
        Assert.Equal("ubag-chatgpt-audio", fetched.UbagModel);
    }
}
