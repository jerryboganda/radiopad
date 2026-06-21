using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers.Ubag;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class UbagProviderAdapterTests
{
    [Fact]
    public async Task CompleteAsync_Blocks_Phi_Request()
    {
        var adapter = new UbagProviderAdapter(new FakeUbagClient());
        var request = new AiCompletionRequest(Provider("gemini_web"), "sys", "patient data", "v1", ContainsPhi: true);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => adapter.CompleteAsync(request, default));

        Assert.Contains("phi_not_supported", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Blocks_Secret_Shaped_Prompt()
    {
        var adapter = new UbagProviderAdapter(new FakeUbagClient());
        var request = new AiCompletionRequest(Provider("gemini_web"), "sys", "Authorization: Bearer token", "v1", ContainsPhi: false);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => adapter.CompleteAsync(request, default));

        Assert.Contains("secret_not_supported", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Rejects_Targets_Outside_Allowlist()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        var adapter = new UbagProviderAdapter(new FakeUbagClient());
        var request = new AiCompletionRequest(Provider("deepseek_web"), "sys", "deidentified prompt", "v1", ContainsPhi: false);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => adapter.CompleteAsync(request, default));

        Assert.Contains("target_not_allowed", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Submits_Allowed_NonPhi_Job()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        var fake = new FakeUbagClient();
        var adapter = new UbagProviderAdapter(fake);
        var request = new AiCompletionRequest(Provider("gemini_web"), "safe system", "deidentified project prompt", "v1", ContainsPhi: false);

        var result = await adapter.CompleteAsync(request, default);

        Assert.Equal("ubag response", result.Text);
        Assert.Equal("gemini_web", fake.CreatedTarget);
        Assert.StartsWith("radiopad-ai-", fake.IdempotencyKey, StringComparison.Ordinal);
    }

    private static ProviderConfig Provider(string model) => new()
    {
        Id = Guid.NewGuid(),
        Name = "UBAG",
        Adapter = UbagProviderAdapter.AdapterId,
        Model = model,
        Compliance = ProviderComplianceClass.Sandbox,
        Enabled = true,
    };

    private sealed class FakeUbagClient : IUbagClient
    {
        public string? CreatedTarget { get; private set; }
        public string? IdempotencyKey { get; private set; }

        public Task<UbagHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(new UbagHealth(true, "ok", "2026-05-22", null));

        public Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct) =>
            Task.FromResult(new UbagBrowserSummary(1, 3, 3, "ready", "{}"));

        public Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UbagTarget>>(new[]
            {
                new UbagTarget("gemini_web", "Gemini", "ready", true, null),
            });

        public Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UbagBrowserContext>>(Array.Empty<UbagBrowserContext>());

        public Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct)
        {
            CreatedTarget = request.Target;
            IdempotencyKey = idempotencyKey;
            return Task.FromResult(new UbagJob("job_1", request.Target, "completed", true, "ubag response", null, null, 25, "{}"));
        }

        public Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct) =>
            Task.FromResult(new UbagJob(jobId, "gemini_web", "completed", true, "ubag response", null, null, 25, "{}"));

        public Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflow("wf_1", "created", "{}"));

        public Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflowRun("run_1", workflowId, "queued", false, null, null, null, "{}"));

        public Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflowRun(runId, "wf_1", "completed", true, "done", null, null, "{}"));
    }
}
