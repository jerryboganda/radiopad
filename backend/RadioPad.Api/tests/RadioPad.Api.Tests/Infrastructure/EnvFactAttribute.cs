using Xunit;

namespace RadioPad.Api.Tests.Infrastructure;

/// <summary>
/// Iter-33 INT-010 — xUnit fact attribute that runs only when a named
/// environment variable matches an expected value (default <c>"1"</c>).
/// Used to gate live SIEM smoke tests behind <c>RADIOPAD_RUN_SIEM_LIVE=1</c>
/// so CI / dev machines without a Splunk/Sentinel/Elastic/Syslog endpoint
/// keep the suite green.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class EnvFactAttribute : FactAttribute
{
    public EnvFactAttribute(string envVar, string expected = "1")
    {
        var actual = Environment.GetEnvironmentVariable(envVar);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Skip = $"Set {envVar}={expected} to run this live smoke test (currently '{actual ?? "<unset>"}').";
        }
    }
}
