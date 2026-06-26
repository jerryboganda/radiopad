using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Exercises the stateless, on-device dictation endpoint
/// <c>POST /api/stt/transcribe</c> over the real HTTP pipeline. In the test build
/// the on-device engine is unconfigured (<c>ILocalSttClient.Available == false</c>),
/// so a well-formed request returns 503 <c>stt_unavailable</c>; malformed requests
/// are rejected with 400 BEFORE the availability gate. The endpoint is anonymous
/// and not report-scoped, so an unauthenticated client reaches it.
/// </summary>
public class SttControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public SttControllerTests(RadioPadAppFactory factory) => _factory = factory;

    private static MultipartFormDataContent AudioForm(byte[] bytes, string contentType, string fileName = "dictation.wav")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", fileName);
        return form;
    }

    [Fact]
    public async Task Valid_Audio_With_No_Engine_Returns_503()
    {
        // Anonymous client (no tenant/user headers) — the endpoint is not
        // report-scoped, so it must still be reachable.
        using var client = _factory.CreateClient();
        using var form = AudioForm(new byte[] { 1, 2, 3, 4 }, "audio/wav");
        var resp = await client.PostAsync("/api/stt/transcribe", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("stt_unavailable", body);
    }

    [Fact]
    public async Task Missing_Audio_Returns_400()
    {
        using var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        var resp = await client.PostAsync("/api/stt/transcribe", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("validation", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unsupported_ContentType_Returns_400()
    {
        using var client = _factory.CreateClient();
        using var form = AudioForm(Encoding.UTF8.GetBytes("not audio"), "text/plain", "note.txt");
        var resp = await client.PostAsync("/api/stt/transcribe", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("unsupported audio content type", await resp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Locks the success-path JSON contract the desktop frontend depends on: with
    /// an available on-device engine, a valid upload returns 200 with
    /// transcript/provider/model/latencyMs and per-word ensemble spans
    /// (text/flagged/reason/source) — byte-for-byte the shape the report-scoped
    /// path emits. Uses a stub ILocalSttClient so it runs in CI without a model.
    /// </summary>
    [Fact]
    public async Task Available_Engine_Returns_200_With_Transcript_And_Spans()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILocalSttClient>();
                services.AddSingleton<ILocalSttClient>(new StubLocalSttClient());
            });
        });
        using var client = factory.CreateClient();
        using var form = AudioForm(new byte[] { 1, 2, 3, 4 }, "audio/wav");
        var resp = await client.PostAsync("/api/stt/transcribe", form);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("chest is clear", root.GetProperty("transcript").GetString());
        Assert.Equal("on-device", root.GetProperty("provider").GetString());
        Assert.Equal("parakeet+whisper", root.GetProperty("model").GetString());
        Assert.Equal(42, root.GetProperty("latencyMs").GetInt64());
        var spans = root.GetProperty("spans");
        Assert.Equal(JsonValueKind.Array, spans.ValueKind);
        Assert.Equal(1, spans.GetArrayLength());
        var span = spans[0];
        Assert.Equal("clear", span.GetProperty("text").GetString());
        Assert.True(span.GetProperty("flagged").GetBoolean());
        Assert.Equal("disagreement", span.GetProperty("reason").GetString());
        Assert.Equal("whisper", span.GetProperty("source").GetString());
    }

    /// <summary>Minimal on-device STT stub: always available, returns a canned
    /// transcript with one flagged ensemble span.</summary>
    private sealed class StubLocalSttClient : ILocalSttClient
    {
        public bool Available => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string contentType, CancellationToken ct, string? mode = null)
            => Task.FromResult(new TranscriptionResult(
                Text: "chest is clear",
                Provider: "on-device",
                Model: "parakeet+whisper",
                LatencyMs: 42,
                Spans: new[] { new ReconciledSpan("clear", true, "disagreement", "whisper") }));
    }
}
