using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Controllers;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Exercises the on-device whole-report generation endpoint
/// (<see cref="LocalGenerationController"/>), which lets the desktop frontend generate a report
/// entirely against the local llama-server when an on-device provider is selected — see
/// frontend/lib/models/onDeviceProvider.ts and NewReportWizard's local-provider branch.
/// </summary>
// Toggles RADIOPAD_LOCAL_STT_ENABLED (a process-global env var), so it must not run in parallel
// with tests that assert the flag is unset. See EnvironmentVariableCollection.
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class LocalGenerationControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public LocalGenerationControllerTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Disabled_By_Default_Returns_503_Not_401()
    {
        // Test build never sets RADIOPAD_LOCAL_STT_ENABLED, so this must be inert (503) — and
        // reachable anonymously at all, proving the RadioPadBearerMiddleware whitelist entry works.
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/local-generation/report", new
        {
            modality = "CT", bodyPart = "KUB", findings = "5mm renal calculus.",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("stt_unavailable", body.GetProperty("kind").GetString());
    }

    private sealed class FakeLlamaAdapter : IAiProviderAdapter
    {
        public string Id => LlamaCppProvider.AdapterId;
        private readonly Func<AiCompletionRequest, Task<AiResult>> _impl;
        public FakeLlamaAdapter(Func<AiCompletionRequest, Task<AiResult>> impl) => _impl = impl;
        public Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct) => _impl(request);
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _var;
        private readonly string? _prev;
        public EnvScope(string var, string value)
        {
            _var = var;
            _prev = Environment.GetEnvironmentVariable(var);
            Environment.SetEnvironmentVariable(var, value);
        }
        public void Dispose() => Environment.SetEnvironmentVariable(_var, _prev);
    }

    [Fact]
    public async Task Enabled_HappyPath_Runs_The_Adapter_Directly_And_Parses_Sections()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        AiCompletionRequest? captured = null;
        const string json = """
            {"indication":"Suspected urolithiasis.","technique":"Non-contrast CT.",
             "findings":"KIDNEYS:\n• Right lower pole calculus.","impression":"1. Non-obstructive calculus.",
             "recommendations":"No specific follow-up is indicated."}
            """;
        var adapter = new FakeLlamaAdapter(req =>
        {
            captured = req;
            return Task.FromResult(new AiResult(json, "llama-cpp", "medgemma", 42, 10, 5, req.PromptVersion));
        });

        var controller = new LocalGenerationController(new[] { adapter }, NullLogger<LocalGenerationController>.Instance);
        var result = await controller.GenerateReport(
            new LocalGenerationController.GenerateReportDto(
                Modality: "CT", BodyPart: "KUB", Contrast: "Without contrast", Age: 45, Gender: "Male",
                Indication: "Flank pain.", Findings: "5mm right renal calculus."),
            CancellationToken.None);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var sections = Assert.IsType<LocalGenerationController.GeneratedReportSections>(ok.Value);
        Assert.Equal("Suspected urolithiasis.", sections.Indication);
        Assert.Contains("Right lower pole calculus", sections.Findings);
        Assert.Equal("1. Non-obstructive calculus.", sections.Impression);
        Assert.Equal("No specific follow-up is indicated.", sections.Recommendations);
        Assert.Equal("medgemma", sections.Model);

        // Never routes through the tenant-aware AiGateway/provider registry — always the same
        // fixed local adapter, and the prompt embeds the raw inputs directly (no rulebook fetch).
        Assert.NotNull(captured);
        Assert.Contains("5mm right renal calculus", captured!.UserPrompt);
        Assert.Contains("Flank pain", captured.UserPrompt);
        Assert.NotNull(captured.Grammar);
    }

    [Fact]
    public async Task Enabled_TransportFailure_Returns_502_With_The_Adapters_Actionable_Message()
    {
        using var env = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var adapter = new FakeLlamaAdapter(req =>
            throw new ProviderTransportException("llama-cpp: HTTP transport failure: Connection refused."));
        var controller = new LocalGenerationController(new[] { adapter }, NullLogger<LocalGenerationController>.Instance);

        var result = await controller.GenerateReport(
            new LocalGenerationController.GenerateReportDto(
                Modality: "CT", BodyPart: "KUB", Contrast: null, Age: null, Gender: null,
                Indication: "", Findings: "x"),
            CancellationToken.None);

        var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, obj.StatusCode);
    }

    [Fact]
    public async Task Enabled_But_No_Llama_Adapter_Registered_Returns_503()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var controller = new LocalGenerationController(
            Array.Empty<IAiProviderAdapter>(), NullLogger<LocalGenerationController>.Instance);

        var result = await controller.GenerateReport(
            new LocalGenerationController.GenerateReportDto(
                Modality: "CT", BodyPart: "KUB", Contrast: null, Age: null, Gender: null,
                Indication: "", Findings: "x"),
            CancellationToken.None);

        var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
    }
}
