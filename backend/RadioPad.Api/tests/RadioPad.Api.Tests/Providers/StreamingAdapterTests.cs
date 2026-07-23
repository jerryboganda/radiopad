using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Providers;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// AI-013 — streaming-path tests for every adapter that supports it (OpenAI-family helper,
/// Anthropic, Ollama NDJSON, llama.cpp SSE). Each uses a canned stream via <see cref="StubHandler"/>
/// so no network is touched. The guarantees under test: deltas arrive in order, final text is the
/// concatenation, token counts come from the provider's usage/final chunk (or fall back to the chunk
/// count), the non-streaming request body is unchanged, and a mid-stream cancel surfaces as
/// <see cref="OperationCanceledException"/>.
/// </summary>
public class StreamingAdapterTests
{
    private static HttpResponseMessage StreamResponse(string body, string contentType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static (List<string> deltas, List<int> tokens, SynchronousProgress<AiStreamChunk> sink) Sink()
    {
        var deltas = new List<string>();
        var tokens = new List<int>();
        var sink = new SynchronousProgress<AiStreamChunk>(c => { deltas.Add(c.Delta); tokens.Add(c.OutputTokens); });
        return (deltas, tokens, sink);
    }

    // ── OpenAI-family helper ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAiHelpers_Streaming_ParsesDeltasInOrderAndUsage()
    {
        const string sse = """
            data: {"choices":[{"delta":{"role":"assistant"}}]}

            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: {"choices":[{"delta":{"content":", world"}}]}

            data: {"choices":[{"delta":{}}]}

            data: {"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":5}}

            data: [DONE]

            """;
        var stub = new StubHandler { Responder = (_, _) => Task.FromResult(StreamResponse(sse, "text/event-stream")) };
        var client = new StubHttpClientFactory(stub).CreateClient("ai");
        var (deltas, tokens, sink) = Sink();

        var (text, pt, ctok, _) = await OpenAiChatHelpers.SendChatStreamingAsync(
            client, "http://localhost/v1/chat/completions", new { }, "openai", sink, CancellationToken.None);

        Assert.Equal(new[] { "Hello", ", world" }, deltas);      // role-only + empty deltas skipped, order kept
        Assert.Equal(new[] { 1, 2 }, tokens);                    // cumulative chunk count per report
        Assert.Equal("Hello, world", text);
        Assert.Equal(12, pt);
        Assert.Equal(5, ctok);
    }

    [Fact]
    public async Task OpenAiHelpers_Streaming_NoUsageChunk_FallsBackToChunkCount()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"a"}}]}

            data: {"choices":[{"delta":{"content":"b"}}]}

            data: [DONE]

            """;
        var stub = new StubHandler { Responder = (_, _) => Task.FromResult(StreamResponse(sse, "text/event-stream")) };
        var client = new StubHttpClientFactory(stub).CreateClient("ai");
        var (_, _, sink) = Sink();

        var (text, pt, ctok, _) = await OpenAiChatHelpers.SendChatStreamingAsync(
            client, "http://localhost/v1/chat/completions", new { }, "openai", sink, CancellationToken.None);

        Assert.Equal("ab", text);
        Assert.Equal(0, pt);       // no usage → prompt tokens unknown
        Assert.Equal(2, ctok);     // completion tokens fall back to the chunk count
    }

    [Fact]
    public void OpenAiHelpers_NoOnStream_SendsStreamFalse()
    {
        // Regression guard for every non-streaming caller: the default body must still carry
        // stream:false and must NOT emit stream_options.
        var body = OpenAiChatHelpers.BuildChatBody("gpt-4o-mini", "sys", "usr");
        var json = JsonSerializer.Serialize(body);
        Assert.Contains("\"stream\":false", json);
        Assert.DoesNotContain("stream_options", json);

        var streamed = JsonSerializer.Serialize(
            OpenAiChatHelpers.BuildChatBody("gpt-4o-mini", "sys", "usr", stream: true));
        Assert.Contains("\"stream\":true", streamed);
        Assert.Contains("stream_options", streamed);
    }

    // ── Anthropic ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anthropic_Streaming_ParsesContentBlockDeltas()
    {
        const string sse = """
            event: message_start
            data: {"type":"message_start","message":{"usage":{"input_tokens":10,"output_tokens":1}}}

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hel"}}

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"lo"}}

            event: message_delta
            data: {"type":"message_delta","usage":{"output_tokens":7}}

            event: message_stop
            data: {"type":"message_stop"}

            """;
        var stub = new StubHandler { Responder = (_, _) => Task.FromResult(StreamResponse(sse, "text/event-stream")) };
        var sut = new AnthropicAiAdapter(new StubHttpClientFactory(stub), NullLogger<AnthropicAiAdapter>.Instance);
        var (deltas, _, sink) = Sink();

        var r = await sut.CompleteAsync(Request("anthropic", model: "claude", sink: sink, apiKeyRef: "test-key"), CancellationToken.None);

        Assert.Equal(new[] { "Hel", "lo" }, deltas);
        Assert.Equal("Hello", r.Text);
        Assert.Equal(10, r.InputTokens);
        Assert.Equal(7, r.OutputTokens);        // cumulative output_tokens from message_delta
    }

    // ── Ollama (NDJSON) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ollama_Streaming_ParsesNdjsonAndFinalCounts()
    {
        const string ndjson =
            "{\"response\":\"Hel\",\"done\":false}\n" +
            "{\"response\":\"lo\",\"done\":false}\n" +
            "{\"response\":\"\",\"done\":true,\"prompt_eval_count\":9,\"eval_count\":4}\n";
        var stub = new StubHandler { Responder = (_, _) => Task.FromResult(StreamResponse(ndjson, "application/x-ndjson")) };
        var sut = new OllamaAiAdapter(new StubHttpClientFactory(stub));
        var (deltas, _, sink) = Sink();

        var r = await sut.CompleteAsync(
            Request("ollama", model: "llama3", endpoint: "http://127.0.0.1:11434/api/generate", sink: sink),
            CancellationToken.None);

        Assert.Equal(new[] { "Hel", "lo" }, deltas);
        Assert.Equal("Hello", r.Text);
        Assert.Equal(9, r.InputTokens);
        Assert.Equal(4, r.OutputTokens);
        Assert.Contains("\"stream\":true", stub.CapturedBodies[0]);
    }

    // ── llama.cpp (SSE) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlamaCpp_Streaming_ParsesSseAndKeepsGrammarInBody()
    {
        const string sse = """
            data: {"content":"Hel","stop":false}

            data: {"content":"lo","stop":false}

            data: {"content":"","stop":true,"tokens_evaluated":11,"tokens_predicted":6}

            """;
        var stub = new StubHandler { Responder = (_, _) => Task.FromResult(StreamResponse(sse, "text/event-stream")) };
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        var (deltas, _, sink) = Sink();

        var req = new AiCompletionRequest(new ProviderConfig
        {
            Name = "llamacpp-local",
            Adapter = LlamaCppProvider.AdapterId,
            Model = "medgemma",
            EndpointUrl = "http://127.0.0.1:8080",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false)
        {
            Grammar = "root ::= \"x\"",
            RepeatPenalty = 1.1,
            OnStream = sink,
        };

        var r = await sut.CompleteAsync(req, CancellationToken.None);

        Assert.Equal(new[] { "Hel", "lo" }, deltas);
        Assert.Equal("Hello", r.Text);
        Assert.Equal(11, r.InputTokens);
        Assert.Equal(6, r.OutputTokens);

        var sentBody = stub.CapturedBodies[0];
        Assert.Contains("\"stream\":true", sentBody);
        Assert.Contains("\"grammar\"", sentBody);
        Assert.Contains("\"repeat_penalty\"", sentBody);
        Assert.Contains("\"stop\"", sentBody);
    }

    [Fact]
    public async Task LlamaCpp_Streaming_CancellationMidStream_ThrowsOCE()
    {
        // Handler yields one SSE event, then the stream blocks until cancelled → the read loop must
        // surface OperationCanceledException, NOT a ProviderTransportException.
        var stub = new StubHandler
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingStream("data: {\"content\":\"Hel\",\"stop\":false}\n\n")),
            }),
        };
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        var (_, _, sink) = Sink();

        var req = new AiCompletionRequest(new ProviderConfig
        {
            Name = "llamacpp-local",
            Adapter = LlamaCppProvider.AdapterId,
            Model = "medgemma",
            EndpointUrl = "http://127.0.0.1:8080",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false)
        { OnStream = sink };

        using var cts = new CancellationTokenSource();
        var task = sut.CompleteAsync(req, cts.Token);
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static AiCompletionRequest Request(
        string adapter, string model, SynchronousProgress<AiStreamChunk> sink,
        string? endpoint = null, string? apiKeyRef = null) =>
        new(new ProviderConfig
        {
            Name = adapter,
            Adapter = adapter,
            Model = model,
            EndpointUrl = endpoint ?? "",
            ApiKeySecretRef = apiKeyRef ?? "",
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false)
        { OnStream = sink };

    /// <summary>A stream that returns a fixed prefix on the first read, then blocks (honouring the
    /// cancellation token) — used to force a mid-stream cancellation.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly byte[] _prefix;
        private int _pos;

        public BlockingStream(string prefix) => _prefix = Encoding.UTF8.GetBytes(prefix);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos < _prefix.Length)
            {
                var n = Math.Min(buffer.Length, _prefix.Length - _pos);
                _prefix.AsSpan(_pos, n).CopyTo(buffer.Span);
                _pos += n;
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
