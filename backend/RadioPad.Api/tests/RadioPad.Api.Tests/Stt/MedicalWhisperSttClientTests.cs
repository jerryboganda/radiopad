using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Audio;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Stt;

/// <summary>
/// Phase 3 — the medical Whisper cross-check engine. A 3rd, decorrelated voice
/// (full large-v3 via whisper.cpp) that self-disables until its model is present,
/// so it is inert on web/server and until the model is provisioned on demand.
/// </summary>
public class MedicalWhisperSttClientTests
{
    [Fact]
    public void EngineId_Is_WhisperMedical()
    {
        var engine = new MedicalWhisperSttClient(
            new WavAudioDecoder(), NullLogger<MedicalWhisperSttClient>.Instance);
        Assert.Equal("whisper_medical", engine.EngineId);
    }

    [Fact]
    public void Is_Dormant_Without_Model()
    {
        // No RADIOPAD_LOCAL_STT_ENABLED and no medical model on disk in the test
        // environment, so the engine must report itself unavailable (never throws).
        var engine = new MedicalWhisperSttClient(
            new WavAudioDecoder(), NullLogger<MedicalWhisperSttClient>.Instance);
        Assert.False(engine.Available);
    }
}
