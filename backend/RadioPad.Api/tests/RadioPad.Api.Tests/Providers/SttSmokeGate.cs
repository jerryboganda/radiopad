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

    /// <summary>
    /// Set environment variables for the duration of a smoke test and put them back afterwards.
    ///
    /// <para>xUnit runs a whole assembly in ONE process, so a smoke test that sets
    /// <c>RADIOPAD_LOCAL_STT_ENABLED=1</c> and walks away silently enables the on-device engine for
    /// every test that follows — which makes the "while disabled" controller tests fail with
    /// confusing assertion errors that have nothing to do with the code under test. CI does not hit
    /// this today only because the smoke jobs run a narrow <c>--filter</c> and the normal job never
    /// sets a model dir (so the gate returns before touching the environment); running the full
    /// suite locally with a model dir present does hit it.</para>
    /// </summary>
    public sealed class EnvScope : IDisposable
    {
        private readonly List<(string Key, string? Previous)> _saved = new();

        public EnvScope Set(string key, string? value)
        {
            _saved.Add((key, Environment.GetEnvironmentVariable(key)));
            Environment.SetEnvironmentVariable(key, value);
            return this;
        }

        public void Dispose()
        {
            // Reverse order so a key set twice ends on its original value.
            for (var i = _saved.Count - 1; i >= 0; i--)
                Environment.SetEnvironmentVariable(_saved[i].Key, _saved[i].Previous);
            _saved.Clear();
        }
    }

    /// <summary>True when the CI job demands a real on-device run (no silent fallbacks).</summary>
    public static bool IsRequired()
    {
        var f = Environment.GetEnvironmentVariable("RADIOPAD_STT_SMOKE_REQUIRE");
        return string.Equals(f, "1", StringComparison.Ordinal)
            || string.Equals(f, "true", StringComparison.OrdinalIgnoreCase);
    }
}
