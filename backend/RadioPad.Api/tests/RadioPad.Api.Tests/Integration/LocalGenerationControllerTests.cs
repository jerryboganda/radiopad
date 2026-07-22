using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Controllers;
using RadioPad.Api.Services;
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
/// Covers both the (deprecated) synchronous <c>POST /report</c> path and the async <c>/jobs</c>
/// endpoints layered on top of <see cref="AiJobRegistry"/> + <see cref="LocalGenerationJobRunner"/>.
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
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
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

    /// <summary>No-op host lifetime: tokens never fire, so a job's only cancellation source in these
    /// tests is an explicit cancel (the 20-min safety ceiling never trips in a unit test).</summary>
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    /// <summary>Builds a controller wired to a real registry + runner over the given fake adapters,
    /// exactly as DI would (no HttpClientFactory / LlamaServerProcess: the runner then treats the
    /// server as unobservable and reports stage "generating").</summary>
    private static (LocalGenerationController controller, AiJobRegistry registry, LocalGenerationJobRunner runner) NewController(
        params IAiProviderAdapter[] adapters)
    {
        var registry = new AiJobRegistry();
        var runner = new LocalGenerationJobRunner(
            registry, adapters, new FakeLifetime(), NullLogger<LocalGenerationJobRunner>.Instance);
        var controller = new LocalGenerationController(
            adapters, registry, runner, NullLogger<LocalGenerationController>.Instance);
        return (controller, registry, runner);
    }

    private static LocalGenerationController.GenerateReportJobDto NewJobDto(Guid correlationId) =>
        new(Modality: "CT", BodyPart: "Chest", Contrast: null, Age: 50, Gender: "Female",
            Indication: "Cough.", Findings: "Clear lungs.", CorrelationId: correlationId);

    // Read the (internal, anonymous) endpoint result objects the same way the ASP.NET pipeline does —
    // serialize by runtime type, then inspect. Robust across the assembly boundary where reflecting
    // over an internal anonymous type would be brittle.
    private static JsonElement AsJson(object o) => JsonSerializer.SerializeToElement(o, o.GetType());

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    private static Guid AcceptedJobId(IActionResult result) =>
        AsJson(Assert.IsType<AcceptedResult>(result).Value!).GetProperty("jobId").GetGuid();

    private static JsonElement Env(LocalGenerationController controller, Guid jobId) =>
        AsJson(Assert.IsType<OkObjectResult>(controller.JobStatus(jobId)).Value!);

    private static string? StageOf(LocalGenerationController controller, Guid jobId) =>
        Str(Env(controller, jobId), "stage");

    private static string? StatusOf(LocalGenerationController controller, Guid jobId) =>
        Str(Env(controller, jobId), "status");

    private static async Task<JsonElement> PollUntilTerminalAsync(LocalGenerationController controller, Guid jobId)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            var env = Env(controller, jobId);
            if (Str(env, "status") != "running") return env;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException("Job did not reach a terminal status in time.");
    }

    private static async Task WaitForStatusAsync(LocalGenerationController controller, Guid jobId, string expected)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            if (StatusOf(controller, jobId) == expected) return;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException($"Job did not reach status '{expected}' in time.");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        if (!condition()) throw new Xunit.Sdk.XunitException("Condition was not met in time.");
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

        var (controller, _, _) = NewController(adapter);
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

        // The on-device path uses its own few-shot, example-led prompt (not
        // ReportingService.BuildStructuredPrompt's prose-only instructions) — empirically the only
        // thing that reliably gets MedGemma to reproduce heading+bullet structure. The example uses
        // bracketed placeholders, not concrete clinical values — a concrete-value version measurably
        // caused the model to leak the example's fabricated finding into unrelated cases. And a
        // moderate repeat penalty is set to prevent the model degenerating into repeating its last
        // line once a long systematic review runs out of new content to add (also verified empirically).
        Assert.Contains("EXAMPLE LAYOUT", captured.UserPrompt);
        Assert.Contains("<PAIRED ORGAN> RIGHT:", captured.UserPrompt);
        Assert.DoesNotContain("8 mm", captured.UserPrompt);
        Assert.DoesNotContain("12 mm", captured.UserPrompt);
        Assert.Equal(1.1, captured.RepeatPenalty);
        Assert.Equal(256, captured.RepeatLastN);
    }

    [Fact]
    public async Task Enabled_TransportFailure_Returns_502_With_The_Adapters_Actionable_Message()
    {
        using var env = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var adapter = new FakeLlamaAdapter(req =>
            throw new ProviderTransportException("llama-cpp: HTTP transport failure: Connection refused."));
        var (controller, _, _) = NewController(adapter);

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

        var (controller, _, _) = NewController();

        var result = await controller.GenerateReport(
            new LocalGenerationController.GenerateReportDto(
                Modality: "CT", BodyPart: "KUB", Contrast: null, Age: null, Gender: null,
                Indication: "", Findings: "x"),
            CancellationToken.None);

        var obj = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
    }

    // ── Async job endpoints ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Job_Endpoints_Are_Inert_503_When_Disabled()
    {
        // No EnvScope: RADIOPAD_LOCAL_STT_ENABLED is unset in the test build, so every job endpoint
        // must be inert (503 stt_unavailable), exactly like the sync endpoint — a hosted build never
        // exposes on-device generation.
        var (controller, _, _) = NewController();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsAssignableFrom<ObjectResult>(controller.SubmitJob(NewJobDto(Guid.NewGuid()))).StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsAssignableFrom<ObjectResult>(controller.JobStatus(Guid.NewGuid())).StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsAssignableFrom<ObjectResult>(controller.ListJobs()).StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsAssignableFrom<ObjectResult>(controller.CancelJob(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public void Submit_Without_CorrelationId_Is_400()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");
        var adapter = new FakeLlamaAdapter(req => Task.FromResult(new AiResult("{}", "llama-cpp", "medgemma", 1, 1, 1, req.PromptVersion)));
        var (controller, _, _) = NewController(adapter);

        var bad = Assert.IsType<BadRequestObjectResult>(controller.SubmitJob(NewJobDto(Guid.Empty)));
        Assert.Equal("correlation_required", Str(AsJson(bad.Value!), "kind"));
    }

    [Fact]
    public void Poll_Unknown_Job_Returns_404_With_Job_Not_Found_Shape()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");
        var (controller, _, _) = NewController();

        var notFound = Assert.IsType<NotFoundObjectResult>(controller.JobStatus(Guid.NewGuid()));
        Assert.Equal("job_not_found", Str(AsJson(notFound.Value!), "kind"));
    }

    [Fact]
    public void Cancel_Unknown_Job_Returns_404()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");
        var (controller, _, _) = NewController();

        var notFound = Assert.IsType<NotFoundObjectResult>(controller.CancelJob(Guid.NewGuid()));
        Assert.Equal("job_not_found", Str(AsJson(notFound.Value!), "kind"));
    }

    [Fact]
    public async Task Job_Submit_Poll_Reaches_Ok_With_The_Parsed_Sections()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        const string json = """
            {"indication":"Ind.","technique":"Tech.","findings":"LUNGS:\n• Clear.",
             "impression":"1. Normal.","recommendations":"None."}
            """;
        var adapter = new FakeLlamaAdapter(req =>
            Task.FromResult(new AiResult(json, "llama-cpp", "medgemma", 42, 10, 5, req.PromptVersion)));
        var (controller, _, _) = NewController(adapter);

        var jobId = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));

        var env = await PollUntilTerminalAsync(controller, jobId);
        Assert.Equal("ok", Str(env, "status"));
        Assert.Equal("local-generate", Str(env, "kind"));
        Assert.Equal("report", Str(env, "mode"));

        // `result` carries the section-shaped payload (PascalCase, GeneratedReportSections).
        var sections = env.GetProperty("result");
        Assert.Contains("Clear", sections.GetProperty("Findings").GetString());
        Assert.Equal("medgemma", sections.GetProperty("Model").GetString());
    }

    [Fact]
    public async Task Resubmitting_The_Same_Correlation_Id_Returns_The_In_Flight_Job()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var release = new TaskCompletionSource();
        var callCount = 0;
        var adapter = new FakeLlamaAdapter(async req =>
        {
            Interlocked.Increment(ref callCount);
            await release.Task;
            return new AiResult("{}", "llama-cpp", "medgemma", 1, 1, 1, req.PromptVersion);
        });
        var (controller, _, _) = NewController(adapter);

        var correlationId = Guid.NewGuid();
        var first = AcceptedJobId(controller.SubmitJob(NewJobDto(correlationId)));
        var second = AcceptedJobId(controller.SubmitJob(NewJobDto(correlationId)));

        // Single-flight: same job id, and the provider was only ever invoked once.
        Assert.Equal(first, second);
        Assert.Equal(1, Volatile.Read(ref callCount));

        release.SetResult();
        await PollUntilTerminalAsync(controller, first);
    }

    [Fact]
    public async Task Two_Jobs_Serialize_Second_Shows_Queued_While_First_Runs()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var release = new TaskCompletionSource();
        var callCount = 0;
        const string json = """{"indication":"","technique":"","findings":"HEART:\n• Normal.","impression":"","recommendations":""}""";
        var adapter = new FakeLlamaAdapter(async req =>
        {
            Interlocked.Increment(ref callCount);
            await release.Task;   // block every call until the test releases
            return new AiResult(json, "llama-cpp", "medgemma", 42, 10, 5, req.PromptVersion);
        });
        var (controller, _, _) = NewController(adapter);

        var job1 = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));
        var job2 = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));

        // The first job holds the single llama-server slot and is generating…
        await WaitForAsync(() => StageOf(controller, job1) == "generating");
        // …so the second is queued behind it — and its provider call has NOT started.
        Assert.Equal("queued", StageOf(controller, job2));
        Assert.Equal(1, Volatile.Read(ref callCount));

        // Release the first: it completes, frees the slot, and the second then runs to completion.
        release.SetResult();
        await PollUntilTerminalAsync(controller, job1);
        var env2 = await PollUntilTerminalAsync(controller, job2);
        Assert.Equal("ok", Str(env2, "status"));
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Cancel_While_Queued_Never_Invokes_The_Provider_And_Marks_Cancelled()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var release = new TaskCompletionSource();
        var callCount = 0;
        var adapter = new FakeLlamaAdapter(async req =>
        {
            Interlocked.Increment(ref callCount);
            await release.Task;
            return new AiResult("{}", "llama-cpp", "medgemma", 1, 1, 1, req.PromptVersion);
        });
        var (controller, _, _) = NewController(adapter);

        var job1 = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));
        var job2 = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));

        await WaitForAsync(() => StageOf(controller, job1) == "generating");
        Assert.Equal("queued", StageOf(controller, job2));

        // Cancel the still-queued second job.
        var cancel = Assert.IsType<AcceptedResult>(controller.CancelJob(job2));
        Assert.True(AsJson(cancel.Value!).GetProperty("cancelRequested").GetBoolean());

        // It transitions straight to "cancelled" — its semaphore wait observed the cancellation, so
        // the provider was never called on its behalf (only the first job's single call happened).
        await WaitForStatusAsync(controller, job2, "cancelled");
        Assert.Equal(1, Volatile.Read(ref callCount));

        release.SetResult();
        await PollUntilTerminalAsync(controller, job1);
    }

    [Fact]
    public async Task Poll_Envelope_Has_The_Same_Keys_As_The_Hosted_Poll_Plus_Stage()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var adapter = new FakeLlamaAdapter(req =>
            Task.FromResult(new AiResult("{}", "llama-cpp", "medgemma", 1, 1, 1, req.PromptVersion)));
        var (controller, _, _) = NewController(adapter);

        var jobId = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));
        var env = await PollUntilTerminalAsync(controller, jobId);

        // Parity with ReportsController.AiJobStatus so the frontend poller is origin-agnostic; the
        // local path adds `stage` (queued | model-loading | generating), computed live.
        var keys = env.EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var hostedKey in new[] { "jobId", "kind", "mode", "status", "elapsedMs", "result", "error", "errorKind" })
            Assert.Contains(hostedKey, keys);
        Assert.Contains("stage", keys);
    }

    [Fact]
    public async Task List_Endpoint_Projects_Jobs_Without_The_Result_Payload()
    {
        // Named (not `_`) — this method also needs `out _` below for TryGetProperty,
        // and a `using var _` binds `_` as a real variable, which can't double as an
        // out-parameter discard in the same scope (CS1657).
        using var envScope = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var adapter = new FakeLlamaAdapter(req =>
            Task.FromResult(new AiResult("{}", "llama-cpp", "medgemma", 1, 1, 1, req.PromptVersion)));
        var (controller, _, _) = NewController(adapter);

        var correlationId = Guid.NewGuid();
        var jobId = AcceptedJobId(controller.SubmitJob(NewJobDto(correlationId)));
        await PollUntilTerminalAsync(controller, jobId);

        var envelope = AsJson(Assert.IsType<OkObjectResult>(controller.ListJobs()).Value!);
        var list = envelope.GetProperty("jobs"); // wrapped to match JobsController.List's { jobs } shape
        Assert.Equal(1, list.GetArrayLength());
        var item = list[0];
        Assert.Equal(jobId, item.GetProperty("jobId").GetGuid());
        Assert.Equal(correlationId, item.GetProperty("reportId").GetGuid());   // the hosted-report correlation id
        // Light list rows carry no result payload — the widget fetches that separately.
        Assert.False(item.TryGetProperty("result", out _));
    }
}
