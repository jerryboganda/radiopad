using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase 3 — the Kyutai/moshi.cpp engine self-disables until its binary + GGUF are
/// provisioned, so the ensemble/cross-check skip it gracefully out of the box.
/// </summary>
public class KyutaiMoshiSttClientTests
{
    [Fact]
    public void EngineId_Is_Kyutai()
    {
        var engine = new KyutaiMoshiSttClient(NullLogger<KyutaiMoshiSttClient>.Instance);
        Assert.Equal("kyutai", engine.EngineId);
    }

    [Fact]
    public void Is_Unavailable_Without_Binary_And_Model()
    {
        // No RADIOPAD_STT_MOSHI_BIN and no GGUF on disk in the test environment.
        var engine = new KyutaiMoshiSttClient(NullLogger<KyutaiMoshiSttClient>.Instance);
        Assert.False(engine.Available);
    }
}
