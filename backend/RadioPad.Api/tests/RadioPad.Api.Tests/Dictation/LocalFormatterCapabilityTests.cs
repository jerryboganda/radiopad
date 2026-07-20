using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// "Can the on-device formatter run here?" and "should it be the DEFAULT?" are two different
/// questions, and they must not share one flag.
///
/// <para>They did. <c>DictationDraftService</c> chose with <c>Available ? local : cloud</c>, while
/// <c>Available</c> was a single env var. That made the two properties impossible to separate: the
/// desktop sidecar could not switch the capability on — which it must, or
/// <c>POST /api/dictation/draft-local</c> answers <c>503 formatter_unavailable</c> and MedGemma is
/// unreachable no matter how thoroughly it is provisioned — without ALSO silently rerouting every
/// report draft on that host away from the cloud formatter, contradicting operator decision D1.</para>
///
/// <para>These tests pin the separation. The sidecar sets the capability flag only; the Rust side
/// asserts the same contract from its end (<c>sidecar_manager::tests</c>).</para>
/// </summary>
[Collection(EnvironmentVariableCollection.Name)]
public class LocalFormatterCapabilityTests
{
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static LocalMedGemmaFormatter Build()
    {
        var provider = new LlamaCppProvider(new SingleClientFactory(), NullLogger<LlamaCppProvider>.Instance);
        return new LocalMedGemmaFormatter(new IAiProviderAdapter[] { provider });
    }

    /// <summary>A plain server/web host enables neither — the on-device endpoint must 503 there.</summary>
    [Fact]
    public void Unconfigured_Host_Offers_Neither_Capability_Nor_Default()
    {
        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, null)
            .Set(LocalMedGemmaFormatter.DefaultEnv, null);

        var formatter = Build();
        Assert.False(formatter.Available);
        Assert.False(formatter.PreferredForReportDrafts);
    }

    /// <summary>
    /// The shipped desktop configuration: capability ON so the on-device endpoint serves, default
    /// OFF so cloud still formats report drafts (D1). This is the exact combination the Tauri
    /// sidecar now launches with.
    /// </summary>
    [Fact]
    public void Desktop_Sidecar_Config_Enables_The_Endpoint_But_Not_The_Default()
    {
        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, "1")
            .Set(LocalMedGemmaFormatter.DefaultEnv, null);

        var formatter = Build();
        Assert.True(formatter.Available, "the on-device endpoint must be reachable on the desktop");
        Assert.False(formatter.PreferredForReportDrafts, "cloud stays the default report formatter (D1)");
    }

    /// <summary>Opting in explicitly is still possible — it just cannot happen by accident.</summary>
    [Fact]
    public void Explicit_Opt_In_Makes_The_Local_Formatter_The_Default()
    {
        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, "1")
            .Set(LocalMedGemmaFormatter.DefaultEnv, "1");

        var formatter = Build();
        Assert.True(formatter.Available);
        Assert.True(formatter.PreferredForReportDrafts);
    }

    /// <summary>
    /// Preferring the local formatter without the capability is incoherent, and must resolve to
    /// "no" rather than routing drafts at a formatter that cannot run.
    /// </summary>
    [Fact]
    public void Default_Without_Capability_Is_Not_Preferred()
    {
        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, null)
            .Set(LocalMedGemmaFormatter.DefaultEnv, "1");

        var formatter = Build();
        Assert.False(formatter.Available);
        Assert.False(formatter.PreferredForReportDrafts);
    }
}
