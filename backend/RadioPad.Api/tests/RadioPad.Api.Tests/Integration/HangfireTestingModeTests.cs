using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-N1 — pins the Testing-environment contract for Hangfire:
///   • <c>AddRadioPadHangfire</c> is skipped, so no Hangfire storage or processing
///     server is registered (tests must never spin one up);
///   • the five migrated job CLASSES are still registered unconditionally, so tests
///     can resolve and invoke their sweep/scan methods directly.
/// </summary>
public class HangfireTestingModeTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public HangfireTestingModeTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public void UnderTesting_NoHangfireStorageOrServerRegistered()
    {
        // JobStorage + IBackgroundJobClient are only registered by AddRadioPadHangfire,
        // which is gated on !IsEnvironment("Testing"). Their absence proves no
        // processing server or schedule registry was created under the test host.
        Assert.Null(_factory.Services.GetService<Hangfire.JobStorage>());
        Assert.Null(_factory.Services.GetService<Hangfire.IBackgroundJobClient>());
    }

    [Fact]
    public void UnderTesting_JobClassesResolveFromDi()
    {
        Assert.NotNull(_factory.Services.GetService<RadioPad.Api.Jobs.RetentionSweepJob>());
        Assert.NotNull(_factory.Services.GetService<RadioPad.Api.Jobs.CriticalResultEscalationJob>());
        Assert.NotNull(_factory.Services.GetService<RadioPad.Api.Jobs.AnomalyScanJob>());
        Assert.NotNull(_factory.Services.GetService<RadioPad.Api.Jobs.OAuthRefreshRotationJob>());
        Assert.NotNull(_factory.Services.GetService<RadioPad.Api.Jobs.ModelDriftDetectionJob>());
    }
}
