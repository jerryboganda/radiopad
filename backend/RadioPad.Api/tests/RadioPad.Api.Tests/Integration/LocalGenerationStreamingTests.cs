using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Controllers;
using RadioPad.Api.Services;
using RadioPad.Api.Tests.Providers;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-B6 — on-device (sidecar) generation streaming. Exercises the registry progress that the
/// <see cref="LocalGenerationJobRunner"/> now feeds from the llama-server token stream, the poll
/// envelope's new <c>progress</c>/<c>partial</c> fields, and the single loopback SSE endpoint
/// <c>GET /api/local-generation/events</c>. Follows <see cref="LocalGenerationControllerTests"/>'s
/// direct-controller pattern (RADIOPAD_LOCAL_STT_ENABLED toggled to enable the gate, a fake llama
/// adapter for the model) — the SSE handler is driven through a <see cref="DefaultHttpContext"/>
/// whose response body is captured for assertion.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class LocalGenerationStreamingTests
{
    // A raw string so the embedded \n stays a two-char JSON escape (valid for ReportSectionJson.Parse),
    // and the bullet + heading survive round-tripping through the streamed chunks and the JSON parser.
    private const string ModelJson =
        """{"indication":"Ind.","technique":"CT.","findings":"KIDNEYS:\n• Right lower pole calculus.","impression":"1. Calc.","recommendations":"None."}""";

    // ── Fakes / helpers (mirrors LocalGenerationControllerTests) ─────────────────────────────────

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

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    /// <summary>Thread-safe write-only capture stream: the SSE loop writes on the thread pool while the
    /// test polls <see cref="Snapshot"/>, so a plain MemoryStream would race.</summary>
    private sealed class CapturingStream : Stream
    {
        private readonly object _lock = new();
        private readonly MemoryStream _inner = new();

        public string Snapshot()
        {
            lock (_lock) return Encoding.UTF8.GetString(_inner.ToArray());
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length { get { lock (_lock) return _inner.Length; } }
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock) _inner.Write(buffer, offset, count);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            lock (_lock) _inner.Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_lock) _inner.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static (LocalGenerationController controller, AiJobRegistry registry, LocalGenerationJobRunner runner) NewController(
        int keepAliveSeconds, params IAiProviderAdapter[] adapters)
    {
        var registry = new AiJobRegistry();
        var runner = new LocalGenerationJobRunner(
            registry, adapters, new FakeLifetime(), NullLogger<LocalGenerationJobRunner>.Instance);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiJobs:SseKeepAliveSeconds"] = keepAliveSeconds.ToString(),
            })
            .Build();
        var controller = new LocalGenerationController(
            adapters, registry, runner, NullLogger<LocalGenerationController>.Instance, new FakeLifetime(), config);
        return (controller, registry, runner);
    }

    private static LocalGenerationController.GenerateReportJobDto NewJobDto(Guid correlationId) =>
        new(Modality: "CT", BodyPart: "KUB", Contrast: "Without contrast", Age: 45, Gender: "Male",
            Indication: "Flank pain.", Findings: "5mm right renal calculus.", CorrelationId: correlationId);

    private static JsonElement AsJson(object o) => JsonSerializer.SerializeToElement(o, o.GetType());

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    private static Guid AcceptedJobId(IActionResult result) =>
        AsJson(Assert.IsType<AcceptedResult>(result).Value!).GetProperty("jobId").GetGuid();

    private static JsonElement Env(LocalGenerationController controller, Guid jobId) =>
        AsJson(Assert.IsType<OkObjectResult>(controller.JobStatus(jobId)).Value!);

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

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        if (!condition()) throw new Xunit.Sdk.XunitException("Condition was not met in time.");
    }

    private static IEnumerable<string> Chunk(string s, int size)
    {
        for (var i = 0; i < s.Length; i += size)
            yield return s.Substring(i, Math.Min(size, s.Length - i));
    }

    /// <summary>Stream ModelJson to the request's sink piece by piece, then block on <paramref name="gate"/>
    /// so a poll/SSE observes the running job with a populated partial buffer before it completes.</summary>
    private static FakeLlamaAdapter StreamingAdapter(TaskCompletionSource gate) =>
        new(async req =>
        {
            var n = 0;
            foreach (var piece in Chunk(ModelJson, 16))
                req.OnStream?.Report(new AiStreamChunk(piece, ++n));
            await gate.Task;
            return new AiResult(ModelJson, "llama-cpp", "medgemma", 42, 10, n, req.PromptVersion);
        });

    private static (DefaultHttpContext context, CapturingStream body) SseContext(CancellationToken aborted)
    {
        var body = new CapturingStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        context.RequestAborted = aborted;
        return (context, body);
    }

    private static void Attach(LocalGenerationController controller, DefaultHttpContext context) =>
        controller.ControllerContext = new ControllerContext { HttpContext = context };

    private static List<(string evt, JsonElement data)> ParseSse(string raw)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? evt = null, data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal)) evt = line["event:".Length..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal)) data = line["data:".Length..].TrimStart();
            }
            if (evt is null || data is null) continue;
            // A live snapshot can catch the final block mid-write (Split keeps the trailing, unterminated
            // segment) → its JSON is truncated. Skip it; the next snapshot sees the completed event.
            try { events.Add((evt, JsonSerializer.Deserialize<JsonElement>(data))); }
            catch (JsonException) { }
        }
        return events;
    }

    private static string ConcatPartials(string raw) =>
        string.Concat(ParseSse(raw).Where(e => e.evt == "partial").Select(e => e.data.GetProperty("delta").GetString()));

    // ── Poll ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalJobPoll_DuringRun_ExposesProgressAndPartial()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var gate = new TaskCompletionSource();
        var (controller, _, _) = NewController(15, StreamingAdapter(gate));

        var jobId = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));

        // The adapter reports every chunk synchronously before blocking on the gate, so the buffer
        // settles to the full model text and stays there while the job runs — a race-free wait.
        await WaitForAsync(() => Str(Env(controller, jobId), "partial") == ModelJson);

        var running = Env(controller, jobId);
        Assert.Equal("running", Str(running, "status"));
        Assert.True(running.GetProperty("progress").GetProperty("tokens").GetInt32() > 0);
        Assert.Equal(ModelJson, Str(running, "partial"));
        Assert.Contains("KIDNEYS", Str(running, "partial")!);

        // Once terminal, progress + partial are cleared (WhenWritingNull in the real pipeline; here the
        // test serializer keeps the key, so assert it is null).
        gate.SetResult();
        var terminal = await PollUntilTerminalAsync(controller, jobId);
        Assert.Equal("ok", Str(terminal, "status"));
        Assert.Equal(JsonValueKind.Null, terminal.GetProperty("progress").ValueKind);
        Assert.Equal(JsonValueKind.Null, terminal.GetProperty("partial").ValueKind);
    }

    // ── SSE stream ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalJobStream_EmitsPartialThenTerminalJobEvent()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        var gate = new TaskCompletionSource();
        var (controller, _, _) = NewController(15, StreamingAdapter(gate));

        var jobId = AcceptedJobId(controller.SubmitJob(NewJobDto(Guid.NewGuid())));

        using var streamCts = new CancellationTokenSource();
        var (context, body) = SseContext(streamCts.Token);
        Attach(controller, context);
        var streamTask = controller.Events(streamCts.Token);

        // Partial deltas arrive while the job is still running (gate held). The buffer is fully
        // populated before the adapter blocks, so the streamed deltas settle to the whole model text.
        await WaitForAsync(() => ConcatPartials(body.Snapshot()) == ModelJson);

        // …then release the model → the job completes → a terminal `job` event with status ok follows.
        gate.SetResult();
        await WaitForAsync(() =>
        {
            var events = ParseSse(body.Snapshot());
            return events.Any(e => e.evt == "job" && e.data.GetProperty("status").GetString() == "ok");
        });

        streamCts.Cancel();
        await streamTask.WaitAsync(TimeSpan.FromSeconds(5));

        var parsed = ParseSse(body.Snapshot());

        // Ordered partial deltas concatenate to the streamed model text (a prefix — here the whole of it).
        var concatenated = ConcatPartials(body.Snapshot());
        Assert.Equal(ModelJson, concatenated);
        Assert.StartsWith(concatenated, ModelJson);

        // The terminal `job` event is emitted AFTER every partial, with status ok.
        var lastPartialIdx = parsed.FindLastIndex(e => e.evt == "partial");
        var okJobIdx = parsed.FindIndex(e => e.evt == "job" && e.data.GetProperty("status").GetString() == "ok");
        Assert.True(okJobIdx > lastPartialIdx);
        Assert.Equal(jobId, parsed[okJobIdx].data.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task LocalJobStream_Gated503_WhenSttDisabled()
    {
        // No EnvScope: RADIOPAD_LOCAL_STT_ENABLED is unset in the test build → the stream is inert.
        var (controller, _, _) = NewController(15);

        using var streamCts = new CancellationTokenSource();
        var (context, _) = SseContext(streamCts.Token);
        Attach(controller, context);

        await controller.Events(streamCts.Token); // gate returns immediately, no loop

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public async Task LocalJobStream_UnknownJobsIdle_EmitsKeepAlive()
    {
        using var _ = new EnvScope("RADIOPAD_LOCAL_STT_ENABLED", "1");

        // Enabled but with no jobs at all → the stream stays open and emits only keep-alive comments
        // (keepAlive shortened to 1 s so the test observes one promptly).
        var (controller, _, _) = NewController(1);

        using var streamCts = new CancellationTokenSource();
        var (context, body) = SseContext(streamCts.Token);
        Attach(controller, context);
        var streamTask = controller.Events(streamCts.Token);

        await WaitForAsync(() => body.Snapshot().Contains(": keep-alive"));

        streamCts.Cancel();
        await streamTask.WaitAsync(TimeSpan.FromSeconds(5));

        var parsed = ParseSse(body.Snapshot());
        Assert.Empty(parsed); // no job/progress/partial events — keep-alive comments only
        Assert.Contains(": keep-alive", body.Snapshot());
    }

    // ── Provider streaming + post-processing (end to end) ────────────────────────────────────────

    [Fact]
    public async Task LlamaCpp_StreamTrue_PostProcessingStillParsesSections()
    {
        // A canned llama-server SSE stream whose `content` chunks concatenate to ModelJson, plus a final
        // stop chunk with token counts. Each chunk is serialized so JSON escaping (\n, the bullet) is
        // handled automatically and re-parses back to the exact substring.
        var sse = new StringBuilder();
        foreach (var piece in Chunk(ModelJson, 20))
            sse.Append("data: ").Append(JsonSerializer.Serialize(new { content = piece, stop = false })).Append("\n\n");
        sse.Append("data: ")
           .Append(JsonSerializer.Serialize(new { content = "", stop = true, tokens_evaluated = 10, tokens_predicted = 30 }))
           .Append("\n\n");

        var stub = new StubHandler
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream"),
            }),
        };
        var provider = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);

        var collected = new List<string>();
        var sink = new SynchronousProgress<AiStreamChunk>(c => collected.Add(c.Delta));
        var dto = new LocalGenerationController.GenerateReportDto(
            Modality: "CT", BodyPart: "KUB", Contrast: "Without contrast", Age: 45, Gender: "Male",
            Indication: "Flank pain.", Findings: "5mm right renal calculus.");

        var result = await provider.CompleteAsync(LocalGenerationController.BuildCompletionRequest(dto, sink), CancellationToken.None);

        // Deltas arrived in order and reconstruct the full model text; token counts come from the stop chunk.
        Assert.Equal(ModelJson, string.Concat(collected));
        Assert.Equal(ModelJson, result.Text);
        Assert.Equal(30, result.OutputTokens);

        // End-of-stream post-processing parses the streamed JSON into structured, formatted sections —
        // the grammar/stop-marker streaming path stays compatible with BuildSections.
        var sections = LocalGenerationController.BuildSections(result);
        Assert.Equal("Ind.", sections.Indication);
        Assert.Contains("KIDNEYS", sections.Findings);
        Assert.Contains("• Right lower pole calculus.", sections.Findings);
        Assert.Equal("1. Calc.", sections.Impression);
    }
}
