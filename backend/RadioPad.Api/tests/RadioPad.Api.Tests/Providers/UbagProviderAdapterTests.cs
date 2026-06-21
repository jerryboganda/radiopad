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

    // ── ProbeAsync context-based readiness ────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_Authenticated_Context_ReturnsOkTrue()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        var fake = new FakeUbagClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "authenticated") });
        var adapter = new UbagProviderAdapter(fake);

        var result = await adapter.ProbeAsync(Provider("gemini_web"), default);

        Assert.True(result.Ok);
        Assert.Equal("authenticated", result.Note);
        Assert.Equal("gemini_web", result.Runtime);
    }

    [Fact]
    public async Task ProbeAsync_Unknown_Context_ReturnsLoginRequired()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "chatgpt_web");
        var fake = new FakeUbagClient(
            targets: new[] { new UbagTarget("chatgpt_web", "ChatGPT", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("chatgpt_web", "unknown") });
        var adapter = new UbagProviderAdapter(fake);

        var result = await adapter.ProbeAsync(Provider("chatgpt_web"), default);

        Assert.False(result.Ok);
        Assert.Contains("login_required:chatgpt_web", result.Error);
        Assert.Equal("unknown", result.Note);
    }

    [Fact]
    public async Task ProbeAsync_No_Context_ReturnsContextNotFound()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        var fake = new FakeUbagClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini", "listed", false, null) },
            contexts: Array.Empty<UbagBrowserContext>());
        var adapter = new UbagProviderAdapter(fake);

        var result = await adapter.ProbeAsync(Provider("gemini_web"), default);

        Assert.False(result.Ok);
        Assert.Contains("context_not_found:gemini_web", result.Error);
    }

    [Fact]
    public async Task ProbeAsync_Target_Not_In_List_ReturnsTargetNotFound()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        // No targets returned at all — probe must surface target_not_found.
        var fake = new FakeUbagClient(
            targets: Array.Empty<UbagTarget>(),
            contexts: Array.Empty<UbagBrowserContext>());
        var adapter = new UbagProviderAdapter(fake);

        var result = await adapter.ProbeAsync(Provider("gemini_web"), default);

        Assert.False(result.Ok);
        Assert.Contains("target_not_found:gemini_web", result.Error);
    }

    [Fact]
    public async Task ProbeAsync_Unhealthy_Gateway_ReturnsOkFalse_BeforeTargetLookup()
    {
        using var env = EnvVarScope.Set("RADIOPAD_UBAG_ALLOWED_TARGETS", "gemini_web");
        var fake = new FakeUbagClient(
            targets: new[] { new UbagTarget("gemini_web", "Gemini", "listed", false, null) },
            contexts: new[] { new UbagBrowserContext("gemini_web", "authenticated") },
            health: new UbagHealth(false, "degraded", null, "gateway_down"));
        var adapter = new UbagProviderAdapter(fake);

        var result = await adapter.ProbeAsync(Provider("gemini_web"), default);

        Assert.False(result.Ok);
        Assert.Equal("gateway_down", result.Error);
        Assert.Equal(UbagProviderAdapter.AdapterId, result.Runtime);
    }

    // ── IsTargetReady helper ──────────────────────────────────────────────────

    [Theory]
    [InlineData("authenticated", true)]
    [InlineData("unknown", false)]
    [InlineData("logged_out", false)]
    public void IsTargetReady_MatchesByLoginState(string loginState, bool expectedReady)
    {
        var contexts = new[] { new UbagBrowserContext("gemini_web", loginState) };
        Assert.Equal(expectedReady, UbagProviderAdapter.IsTargetReady("gemini_web", contexts));
    }

    [Fact]
    public void IsTargetReady_CaseInsensitiveTargetIdMatch()
    {
        var contexts = new[] { new UbagBrowserContext("Gemini_Web", "authenticated") };
        Assert.True(UbagProviderAdapter.IsTargetReady("gemini_web", contexts));
    }

    [Fact]
    public void IsTargetReady_ReturnsFalse_WhenNoMatchingContext()
    {
        var contexts = new[] { new UbagBrowserContext("deepseek_web", "authenticated") };
        Assert.False(UbagProviderAdapter.IsTargetReady("gemini_web", contexts));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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

        private readonly UbagHealth _health;
        private readonly IReadOnlyList<UbagTarget> _targets;
        private readonly IReadOnlyList<UbagBrowserContext> _contexts;

        /// <summary>Default constructor — healthy gateway, gemini_web target, empty contexts.</summary>
        public FakeUbagClient()
            : this(
                targets: new[] { new UbagTarget("gemini_web", "Gemini", "ready", true, null) },
                contexts: Array.Empty<UbagBrowserContext>())
        { }

        public FakeUbagClient(
            IReadOnlyList<UbagTarget> targets,
            IReadOnlyList<UbagBrowserContext> contexts,
            UbagHealth? health = null)
        {
            _health = health ?? new UbagHealth(true, "ok", "2026-05-22", null);
            _targets = targets;
            _contexts = contexts;
        }

        public Task<UbagHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(_health);

        public Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct) =>
            Task.FromResult(new UbagBrowserSummary(1, 3, 3, "ready", "{}"));

        public Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct) =>
            Task.FromResult(_targets);

        public Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct) =>
            Task.FromResult(_contexts);

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
