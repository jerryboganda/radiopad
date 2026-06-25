using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Infrastructure.Providers.Ubag;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// Phase B (dictation transcription) — unit tests for
/// <see cref="UbagClient.CreateTranscriptionJobAsync"/> (request body shape)
/// and <see cref="UbagClient.UploadJobArtifactAsync"/> (binary PUT). HTTP is
/// stubbed via <see cref="StubHandler"/> wired to a BaseAddress-bearing
/// <see cref="IHttpClientFactory"/>; no network is touched.
/// </summary>
public class UbagClientTranscriptionTests
{
    private static UbagClient BuildClient(StubHandler handler)
    {
        var factory = new BaseAddressFactory(handler, "http://localhost/");
        return new UbagClient(factory, NullLogger<UbagClient>.Instance);
    }

    /// <summary>
    /// Wraps the shared <see cref="StubHandler"/> with a BaseAddress so
    /// <see cref="UbagClient"/>'s relative-path requests resolve. (The factory
    /// in UbagClientParsingTests is private to that class, so we declare a
    /// local one here.)
    /// </summary>
    private sealed class BaseAddressFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _baseAddress;

        public BaseAddressFactory(HttpMessageHandler handler, string baseAddress)
        {
            _handler = handler;
            _baseAddress = baseAddress;
        }

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri(_baseAddress) };
    }

    private const string CreatedJobJson = """
        {
          "id": "job_tx_123",
          "target": "gemini_web",
          "status": "queued"
        }
        """;

    private const string ArtifactJson = """
        {
          "job_id": "job_tx_123",
          "key": "dictation.webm",
          "content_type": "audio/webm",
          "size_bytes": 4,
          "checksum": "sha256:abcd"
        }
        """;

    [Fact]
    public async Task CreateTranscriptionJobAsync_Sends_MedicalTranscription_CommandType()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, CreatedJobJson);
        var sut = BuildClient(handler);

        await sut.CreateTranscriptionJobAsync(
            new UbagTranscriptionRequest("gemini_web", "dictation.webm", "transcribe please"),
            "idem-1", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
        var job = doc.RootElement.GetProperty("job");
        Assert.Equal("medical_transcription", job.GetProperty("command_type").GetString());
    }

    [Fact]
    public async Task CreateTranscriptionJobAsync_Sends_AudioArtifactKey_And_WaitForArtifacts()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, CreatedJobJson);
        var sut = BuildClient(handler);

        await sut.CreateTranscriptionJobAsync(
            new UbagTranscriptionRequest("gemini_web", "dictation.webm", "transcribe please"),
            "idem-1", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
        var job = doc.RootElement.GetProperty("job");

        // input carries prompt + audio_artifact_key
        var input = job.GetProperty("input");
        Assert.Equal("transcribe please", input.GetProperty("prompt").GetString());
        Assert.Equal("dictation.webm", input.GetProperty("audio_artifact_key").GetString());

        // options carries return_mode + wait_for_artifacts: ["dictation.webm"]
        var options = job.GetProperty("options");
        Assert.Equal("final", options.GetProperty("return_mode").GetString());
        var wait = options.GetProperty("wait_for_artifacts");
        Assert.Equal(JsonValueKind.Array, wait.ValueKind);
        Assert.Single(wait.EnumerateArray());
        Assert.Equal("dictation.webm", wait[0].GetString());
    }

    [Fact]
    public async Task CreateTranscriptionJobAsync_Does_Not_Send_Temperature()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, CreatedJobJson);
        var sut = BuildClient(handler);

        await sut.CreateTranscriptionJobAsync(
            new UbagTranscriptionRequest("gemini_web", "dictation.webm", "p"),
            "idem-1", CancellationToken.None);

        Assert.DoesNotContain("temperature", handler.CapturedBodies[0]);
    }

    [Fact]
    public async Task CreateTranscriptionJobAsync_Posts_To_Jobs_With_IdempotencyKey()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, CreatedJobJson);
        var sut = BuildClient(handler);

        var job = await sut.CreateTranscriptionJobAsync(
            new UbagTranscriptionRequest("gemini_web", "dictation.webm", "p"),
            "idem-xyz", CancellationToken.None);

        var req = handler.Captured[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("http://localhost/v1/jobs", req.RequestUri!.AbsoluteUri);
        Assert.Equal("idem-xyz", req.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("job_tx_123", job.Id);
    }

    [Fact]
    public async Task UploadJobArtifactAsync_Puts_To_Artifact_Path_With_Content_Headers()
    {
        var handler = StubHandler.Json(HttpStatusCode.Created, ArtifactJson);
        var sut = BuildClient(handler);

        var bytes = new byte[] { 1, 2, 3, 4 };
        using var stream = new MemoryStream(bytes);
        var artifact = await sut.UploadJobArtifactAsync(
            "job_tx_123", "dictation.webm", stream, "audio/webm", bytes.Length, "idem-art", CancellationToken.None);

        var req = handler.Captured[0];
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal("http://localhost/v1/jobs/job_tx_123/artifacts/dictation.webm", req.RequestUri!.AbsoluteUri);
        Assert.Equal("audio/webm", req.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(bytes.Length, req.Content.Headers.ContentLength);
        Assert.Equal("idem-art", req.Headers.GetValues("Idempotency-Key").Single());

        // 201 body parsed into the artifact descriptor.
        Assert.Equal("job_tx_123", artifact.JobId);
        Assert.Equal("dictation.webm", artifact.Key);
        Assert.Equal("audio/webm", artifact.ContentType);
        Assert.Equal(4, artifact.SizeBytes);
        Assert.Equal("sha256:abcd", artifact.Checksum);
    }

    [Fact]
    public async Task UploadJobArtifactAsync_Escapes_Path_Segments()
    {
        var handler = StubHandler.Json(HttpStatusCode.Created, ArtifactJson);
        var sut = BuildClient(handler);

        using var stream = new MemoryStream(new byte[] { 9 });
        await sut.UploadJobArtifactAsync(
            "job id/with spaces", "dictation.webm", stream, "audio/webm", 1, "idem", CancellationToken.None);

        var req = handler.Captured[0];
        // jobId is URL-escaped so the slash + space do not corrupt the path.
        Assert.Equal(
            "http://localhost/v1/jobs/job%20id%2Fwith%20spaces/artifacts/dictation.webm",
            req.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task UploadJobArtifactAsync_NonSuccess_Throws_ProviderTransportException()
    {
        var handler = StubHandler.Json(HttpStatusCode.RequestEntityTooLarge, "{\"error\":\"too big\"}");
        var sut = BuildClient(handler);

        using var stream = new MemoryStream(new byte[] { 1 });
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() =>
            sut.UploadJobArtifactAsync("job_1", "dictation.webm", stream, "audio/webm", 1, "idem", CancellationToken.None));

        Assert.Equal(413, ex.StatusCode);
    }
}
