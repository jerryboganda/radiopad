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
/// (GitHub Copilot CLI / Gemini CLI / Codex CLI) plus the missing-API-key
/// policy added to <see cref="OpenAiCompatibleProvider"/>. All tests use a
/// stub <see cref="IProcessLauncher"/> or <see cref="StubHandler"/> so no
/// real binary or network is touched.
/// </summary>
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
    // GitHub Copilot CLI
    // -----------------------------------------------------------------
    [Fact]
    public async Task GhCopilot_HappyPath_ReturnsStdout_AndPipesPromptOnStdin()
    {
        var stub = StubLauncher.Ok("Suggested: review the lung bases.\n");
        var sut = new GitHubCopilotCliProvider(stub, NullLogger<GitHubCopilotCliProvider>.Instance);

        var r = await sut.CompleteAsync(Request(GitHubCopilotCliProvider.AdapterId), CancellationToken.None);

        Assert.Equal("Suggested: review the lung bases.", r.Text);
        Assert.Single(stub.Captured);
        var spec = stub.Captured[0];
        Assert.Equal("gh", spec.FileName);
        Assert.Contains("copilot", spec.Arguments);
        Assert.NotNull(spec.StandardInput);
        Assert.Contains("summarise this CT chest", spec.StandardInput!);
        // Prompt must never be passed as a process argument (shell-injection guard).
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("summarise this CT chest"));
    }

    [Fact]
    public async Task GhCopilot_BinaryNotFound_ThrowsProviderTransport()
    {
        var sut = new GitHubCopilotCliProvider(StubLauncher.NotFound(), NullLogger<GitHubCopilotCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(GitHubCopilotCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public async Task GhCopilot_Timeout_ThrowsProviderTransport()
    {
        var sut = new GitHubCopilotCliProvider(StubLauncher.Timeout(), NullLogger<GitHubCopilotCliProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(GitHubCopilotCliProvider.AdapterId), CancellationToken.None));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public void GhCopilot_DefaultComplianceClass_IsSandbox()
    {
        Assert.Equal(ProviderComplianceClass.Sandbox, GitHubCopilotCliProvider.DefaultComplianceClass);
        Assert.Equal("github-copilot-cli", GitHubCopilotCliProvider.AdapterId);
    }

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
        Assert.Contains("--model", spec.Arguments);
        Assert.Contains("gemini-1.5-pro", spec.Arguments);
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
    public void Gemini_DefaultComplianceClass_IsSandbox()
    {
        Assert.Equal(ProviderComplianceClass.Sandbox, GeminiCliProvider.DefaultComplianceClass);
        Assert.Equal("gemini-cli", GeminiCliProvider.AdapterId);
    }

    // -----------------------------------------------------------------
    // Codex CLI
    // -----------------------------------------------------------------
    [Fact]
    public async Task Codex_HappyPath_PipesPromptOnStdin()
    {
        var stub = StubLauncher.Ok("codex says hi\n");
        var sut = new CodexCliProvider(stub, NullLogger<CodexCliProvider>.Instance);

        var r = await sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None);

        Assert.Equal("codex says hi", r.Text);
        var spec = stub.Captured[0];
        Assert.Equal("codex", spec.FileName);
        Assert.Contains("--stdin", spec.Arguments);
        Assert.Contains("summarise this CT chest", spec.StandardInput!);
    }

    [Fact]
    public async Task Codex_BinaryNotFound_ThrowsProviderTransport()
    {
        var sut = new CodexCliProvider(StubLauncher.NotFound(), NullLogger<CodexCliProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));
    }

    [Fact]
    public async Task Codex_Timeout_ThrowsProviderTransport()
    {
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
        var sut = new CodexCliProvider(StubLauncher.Ok("x"), NullLogger<CodexCliProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(
            () => sut.CompleteAsync(Request(CodexCliProvider.AdapterId), CancellationToken.None));
        Assert.Contains("cli_binary_not_allowed", ex.Message);
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
            EndpointUrl = "https://api.example.com",
            ApiKeySecretRef = "env:ITER36_COMPAT_KEY",
            Compliance = ProviderComplianceClass.Sandbox,
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
            EndpointUrl = "https://api.example.com",
            ApiKeySecretRef = "env:ITER36_MISSING_KEY",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Contains("api_key_missing", ex.Message);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
