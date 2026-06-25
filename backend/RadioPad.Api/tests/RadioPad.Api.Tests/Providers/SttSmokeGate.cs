using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Shared gating for the real-engine on-device STT smoke tests. The smoke tests
/// are no-ops in normal CI (Linux, no model / native lib) so they don't break the
/// fast lane. But a no-op that silently passes is a FALSE GREEN: if the dedicated
/// Windows smoke job mis-wires a model path, the test would "pass" without ever
/// loading an engine. To prevent that, the Windows job sets
/// <c>RADIOPAD_STT_SMOKE_REQUIRE=1</c>, which turns a missing model dir into a
/// hard failure instead of a skip.
/// </summary>
internal static class SttSmokeGate
{
    /// <summary>
    /// Resolve the model dir from <paramref name="envVar"/>. Returns null to skip
    /// when unset (normal CI). When <c>RADIOPAD_STT_SMOKE_REQUIRE</c> is truthy,
    /// a missing/absent dir fails the test loudly rather than skipping.
    /// </summary>
    public static string? DirOrSkip(string envVar)
    {
        var dir = Environment.GetEnvironmentVariable(envVar);
        var ok = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir);
        if (ok) return dir;

        if (IsRequired())
            Assert.Fail(
                $"{envVar} is unset or points at a missing directory ('{dir}'), " +
                "but RADIOPAD_STT_SMOKE_REQUIRE=1 demands a real on-device run.");
        return null;
    }

    /// <summary>True when the CI job demands a real on-device run (no silent fallbacks).</summary>
    public static bool IsRequired()
    {
        var f = Environment.GetEnvironmentVariable("RADIOPAD_STT_SMOKE_REQUIRE");
        return string.Equals(f, "1", StringComparison.Ordinal)
            || string.Equals(f, "true", StringComparison.OrdinalIgnoreCase);
    }
}
