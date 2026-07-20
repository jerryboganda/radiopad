using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using RadioPad.Infrastructure.Providers.Cli;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// Iter-36 AI-012 — CLI-spawning provider adapter tests
/// (Gemini CLI / Codex CLI) plus the missing-API-key
/// policy added to <see cref="OpenAiCompatibleProvider"/>. All tests use a
/// stub <see cref="IProcessLauncher"/> or <see cref="StubHandler"/> so no
/// real binary or network is touched.
/// </summary>
[Collection(EnvironmentVariableCollection.Name)]
public class Iter36CliProviderTests
{
    // -----------------------------------------------------------------
    // Stub launcher
    // -----------------------------------------------------------------
    private sealed class StubLauncher : IProcessLauncher
    {
        public Func<ProcessLaunchSpec, CancellationToken, Task<ProcessLaunchResult>>? Responder { get; set; }
        public List<ProcessLaunchSpec> Captured { get; } = new();

        public Task<ProcessLaunchResult> RunAsync(ProcessLaunchSpec spec, CancellationToken ct)
        {
            Captured.Add(spec);
            if (Responder is null) throw new InvalidOperationException("StubLauncher.Responder not set");
            return Responder(spec, ct);
        }

        public static StubLauncher Ok(string stdout, int elapsedMs = 12)
            => new()
            {
                Responder = (_, _) => Task.FromResult(new ProcessLaunchResult(0, stdout, "", elapsedMs)),
            };

        public static StubLauncher NotFound()
            => new()
            {
                Responder = (s, _) => throw new ProcessLaunchNotFoundException($"binary '{s.FileName}' not found"),
            };

        public static StubLauncher Timeout()
            => new()
            {
                Responder = (_, _) => throw new ProcessLaunchTimeoutException("timed out", 60_000),
            };
    }

    private static AiCompletionRequest Request(string adapter, string? model = null) =>
        new(new ProviderConfig
        {
            Name = adapter + "-test",
            Adapter = adapter,
            Model = model ?? "",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "be helpful", "summarise this CT chest", "v1", ContainsPhi: false);

    // -----------------------------------------------------------------
    // Gemini CLI
    // -----------------------------------------------------------------
    [Fact]
    public async Task Gemini_HappyPath_PassesModelFlag_WhenConfigured()
    {
        var stub = StubLauncher.Ok("ok response\n");
        var sut = new GeminiCliProvider(stub, NullLogger<GeminiCliProvider>.Instance);

        var r = await sut.CompleteAsync(Request(GeminiCliProvider.AdapterId, model: "gemini-1.5-pro"), CancellationToken.None);

        Assert.Equal("ok response", r.Text);
        var spec = stub.Captured[0];
        Assert.Equal("gemini", spec.FileName);
        // --skip-trust: gemini runs headless in a scrubbed env + temp cwd, so the
        // "trusted folder" check must be bypassed (else exit 55).
        Assert.Contains("--skip-trust", spec.Arguments);
        Assert.Contains("--output-format", spec.Arguments);
        Assert.Contains("json", spec.Arguments);
        Assert.Contains("--model", spec.Arguments);
        Assert.Contains("gemini-1.5-pro", spec.Arguments);
        Assert.DoesNotContain("prompt", spec.Arguments);
        Assert.DoesNotContain("--stdin", spec.Arguments);
        Assert.Contains("summarise this CT chest", spec.StandardInput!);
    }

    [Fact]
    public async Task Gemini_JsonOutput_ExtractsResponseText()
    {
        var stub = StubLauncher.Ok("{\"response\":\"structured response\",\"stats\":{\"inputTokens\":4}}\n");
        var sut = new GeminiCliProvider(stub, NullLogger<GeminiCliProvider>.Instance);

        var r = await sut.CompleteAsync(Request(GeminiCliProvider.AdapterId), CancellationToken.None);

        Assert.Equal("structured response", r.Text);
    }

    [Fact]
    public async Task Gemini_HealthProbe_UsesSkipTrust_AndGenerousTimeout()
    {
        // Regression guard (2026-07-13): `gemini --version` cold-loads the whole
        // Node bundle (~15–31 s in prod), so the old 10 s probe cap always reported
        // the provider "Unreachable". The probe now runs under the generous probe
        // timeout and passes --skip-trust so the trusted-folder check can't fail it.
        var stub = StubLauncher.Ok("0.50.0\n");
        var sut = new GeminiCliProvider(stub, NullLogger<GeminiCliProvider>.Instance);

        var health = await sut.ProbeAsync(
            new ProviderConfig { Adapter = GeminiCliProvider.AdapterId }, CancellationToken.None);

        Assert.True(health.Ok);
        var spec = stub.Captured[0];
        Assert.Contains("--skip-trust", spec.Arguments);
        Assert.Contains("--version", spec.Arguments);
        Assert.True(spec.TimeoutMs > 10_000,
            $"probe timeout {spec.TimeoutMs}ms must exceed the old 10s cap that false-negatived gemini");
    }

    [Fact]
    public async Task Gemini_HealthProbe_ReportsMissingBinary()
    {
        var sut = new GeminiCliProvider(StubLauncher.NotFound(), NullLogger<GeminiCliProvider>.Instance);

        var health = await sut.ProbeAsync(new ProviderConfig { Adapter = GeminiCliProvider.AdapterId }, CancellationToken.None);

        Assert.False(health.Ok);
        Assert.Contains("not found", health.Error);
        Assert.Equal("gemini", health.Runtime);
    }

    [Fact]
    public async Task Gemini_BinaryNotFound_ThrowsProviderTransport()
    {
        var sut = new GeminiCliProvider(StubLauncher.NotFound(), NullLogger<GeminiCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(GeminiCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public async Task Gemini_Timeout_ThrowsProviderTransport()
    {
        var sut = new GeminiCliProvider(StubLauncher.Timeout(), NullLogger<GeminiCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(GeminiCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public void Gemini_DefaultComplianceClass_IsPhiApproved()
    {
        // Operator promotion 2026-07-12 — mirrors the UBAG PhiApproved decision.
        Assert.Equal(ProviderComplianceClass.PhiApproved, GeminiCliProvider.DefaultComplianceClass);
        Assert.Equal("gemini-cli", GeminiCliProvider.AdapterId);
    }

    [Fact]
    public void Launcher_EnvAllowlist_PassesGeminiApiKey()
    {
        // Regression guard (2026-07-13): the launcher scrubs the child env to an
        // allow-list; if GEMINI_API_KEY is dropped, gemini-cli finds no key, falls
        // back to the retired OAuth path, and exits 41 (FatalAuthenticationError) —
        // the exact "gemini exited with code 41" the operator hit.
        Assert.Contains("GEMINI_API_KEY", DefaultProcessLauncher.BaseEnvAllowlist);
        Assert.Contains("GOOGLE_API_KEY", DefaultProcessLauncher.BaseEnvAllowlist);
        // Trust-workspace flag: without it gemini-cli aborts headless with exit 55.
        Assert.Contains("GEMINI_CLI_TRUST_WORKSPACE", DefaultProcessLauncher.BaseEnvAllowlist);
    }

    // -----------------------------------------------------------------
    // Codex CLI
    // -----------------------------------------------------------------
    [Fact]
    public async Task Codex_HappyPath_PipesPromptOnStdin()
    {
        using var env = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var stub = StubLauncher.Ok("codex says hi\n");
        var sut = new CodexCliProvider(stub, NullLogger<CodexCliProvider>.Instance);

        var r = await sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None);

        Assert.Equal("codex says hi", r.Text);
        var spec = stub.Captured[0];
        Assert.Equal("codex", spec.FileName);
        // Prompt is piped via StandardInput and the adapter must not opt into
        // agentic/full-auto modes by default.
        Assert.Contains("exec", spec.Arguments);
        Assert.Contains("--sandbox", spec.Arguments);
        Assert.Contains("read-only", spec.Arguments);
        Assert.Contains("-", spec.Arguments);
        Assert.DoesNotContain("--full-auto", spec.Arguments);
        Assert.Contains("summarise this CT chest", spec.StandardInput!);
    }

    [Fact]
    public async Task Codex_FailsClosed_UnlessExplicitlyEnabled()
    {
        using var env = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", null);
        var stub = StubLauncher.Ok("never reached");
        var sut = new CodexCliProvider(stub, NullLogger<CodexCliProvider>.Instance);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));

        Assert.Contains("runtime_not_enabled", ex.Message);
        Assert.Empty(stub.Captured);
    }

    [Fact]
    public async Task Codex_BinaryNotFound_ThrowsProviderTransport()
    {
        using var env = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var sut = new CodexCliProvider(StubLauncher.NotFound(), NullLogger<CodexCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public async Task Codex_Timeout_ThrowsProviderTransport()
    {
        using var env = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var sut = new CodexCliProvider(StubLauncher.Timeout(), NullLogger<CodexCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public void Codex_DefaultComplianceClass_IsSandbox()
    {
        Assert.Equal(ProviderComplianceClass.Sandbox, CodexCliProvider.DefaultComplianceClass);
        Assert.Equal("codex-cli", CodexCliProvider.AdapterId);
    }

    // -----------------------------------------------------------------
    // Cross-cutting: prompt sanitation, allowlist
    // -----------------------------------------------------------------
    [Fact]
    public async Task ControlCharacters_InPrompt_AreRefused()
    {
        var stub = StubLauncher.Ok("never reached");
        var sut = new GeminiCliProvider(stub, NullLogger<GeminiCliProvider>.Instance);

        var bad = new AiCompletionRequest(
            new ProviderConfig { Name = "g", Adapter = GeminiCliProvider.AdapterId, Compliance = ProviderComplianceClass.LocalOnly, Enabled = true },
            "ok", "evil\u0000prompt", "v1", false);

        await Assert.ThrowsAsync<ProviderPolicyException>(() => sut.CompleteAsync(bad, CancellationToken.None));
        Assert.Empty(stub.Captured);
    }

    [Fact]
    public async Task BinaryAllowlist_DeniesUnlistedBinary()
    {
        using var env = EnvVarScope.Set("RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS", "/usr/bin/never-matches");
        using var codex = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var sut = new CodexCliProvider(StubLauncher.Ok("x"), NullLogger<CodexCliProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));
        Assert.Contains("cli_binary_not_allowed", ex.Message);
    }

    [Fact]
    public async Task ProductionCliProvider_RequiresBinaryAllowlist()
    {
        using var aspnet = EnvVarScope.Set("ASPNETCORE_ENVIRONMENT", "Production");
        using var allowed = EnvVarScope.Set("RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS", null);
        using var codex = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var sut = new CodexCliProvider(StubLauncher.Ok("x"), NullLogger<CodexCliProvider>.Instance);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));

        Assert.Contains("cli_binary_allowlist_required", ex.Message);
    }

    [Fact]
    public async Task Cli_Providers_Allow_Phi()
    {
        static AiCompletionRequest PhiRequest(string adapter) => new(
            new ProviderConfig
            {
                Name = "cli",
                Adapter = adapter,
                Compliance = ProviderComplianceClass.PhiApproved,
                Enabled = true,
            },
            "sys",
            "synthetic patient context",
            "v1",
            ContainsPhi: true);

        // PHI gate removed (operator decision 2026-07-20): every CLI adapter
        // accepts PHI.
        var gemini = new GeminiCliProvider(StubLauncher.Ok("phi ok"), NullLogger<GeminiCliProvider>.Instance);
        var r = await gemini.CompleteAsync(PhiRequest(GeminiCliProvider.AdapterId), CancellationToken.None);
        Assert.Equal("phi ok", r.Text);

        using var env = EnvVarScope.Set("RADIOPAD_CODEX_CLI_ENABLED", "1");
        var codex = new CodexCliProvider(StubLauncher.Ok("phi ok too"), NullLogger<CodexCliProvider>.Instance);
        var r2 = await codex.CompleteAsync(PhiRequest(CodexCliProvider.AdapterId), CancellationToken.None);
        Assert.Equal("phi ok too", r2.Text);
    }

    // -----------------------------------------------------------------
    // OpenAiCompatibleProvider — iter-36 additions
    // -----------------------------------------------------------------
    [Fact]
    public async Task OpenAiCompatible_5xx_ThrowsTransport()
    {
        using var env = EnvVarScope.Set("ITER36_COMPAT_KEY", "k");
        var stub = StubHandler.Json(HttpStatusCode.BadGateway, "{\"error\":\"upstream\"}");
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        var req = new AiCompletionRequest(new ProviderConfig
        {
            Name = "compat",
            Adapter = OpenAiCompatibleProvider.AdapterId,
            Model = "m",
            EndpointUrl = "http://127.0.0.1:11434",
            ApiKeySecretRef = "env:ITER36_COMPAT_KEY",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task OpenAiCompatible_MissingApiKey_ThrowsPolicy()
    {
        // Env var is intentionally NOT set — secret ref points at it.
        using var env = EnvVarScope.Set("ITER36_MISSING_KEY", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, "{}");
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        var req = new AiCompletionRequest(new ProviderConfig
        {
            Name = "compat",
            Adapter = OpenAiCompatibleProvider.AdapterId,
            Model = "m",
            EndpointUrl = "http://127.0.0.1:11434",
            ApiKeySecretRef = "env:ITER36_MISSING_KEY",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Contains("api_key_missing", ex.Message);
    }

    [Fact]
    public async Task OpenAiCompatible_PrivateEndpoint_Blocked_For_NonLocalOnly()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, "{}");
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        var req = new AiCompletionRequest(new ProviderConfig
        {
            Name = "compat",
            Adapter = OpenAiCompatibleProvider.AdapterId,
            Model = "m",
            EndpointUrl = "http://127.0.0.1:11434",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Contains("endpoint_private_network_blocked", ex.Message);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
