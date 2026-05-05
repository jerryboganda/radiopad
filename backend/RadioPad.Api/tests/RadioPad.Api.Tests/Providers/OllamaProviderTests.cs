using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class OllamaProviderTests
{
    private const string OkBody = """
        {"model":"llama3.1","message":{"role":"assistant","content":"local reply"},"prompt_eval_count":7,"eval_count":3}
        """;

    private static AiCompletionRequest Request(string baseUrl) =>
        new(new ProviderConfig
        {
            Name = "ollama-chat",
            Adapter = OllamaProvider.AdapterId,
            Model = "llama3.1",
            EndpointUrl = baseUrl,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Posts_To_ApiChat_And_Parses_Counts()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OllamaProvider(new StubHttpClientFactory(stub), NullLogger<OllamaProvider>.Instance);
        var r = await sut.CompleteAsync(Request("http://127.0.0.1:11434"), CancellationToken.None);

        Assert.Equal("local reply", r.Text);
        Assert.Equal(7, r.InputTokens);
        Assert.Equal(3, r.OutputTokens);
        Assert.Equal("http://127.0.0.1:11434/api/chat", stub.Captured[0].RequestUri!.ToString());
        // Stream:false must be in the body so we don't get NDJSON.
        Assert.Contains("\"stream\":false", stub.CapturedBodies[0]);
    }

    [Fact]
    public async Task DefaultEndpoint_Is_Localhost_When_Empty()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OllamaProvider(new StubHttpClientFactory(stub), NullLogger<OllamaProvider>.Instance);
        await sut.CompleteAsync(Request(""), CancellationToken.None);
        Assert.Equal("http://127.0.0.1:11434/api/chat", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        var stub = StubHandler.Json(HttpStatusCode.ServiceUnavailable, "down");
        var sut = new OllamaProvider(new StubHttpClientFactory(stub), NullLogger<OllamaProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request("http://127.0.0.1:11434"), CancellationToken.None));
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task Probe_Targets_ApiTags()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, "{\"models\":[]}");
        var sut = new OllamaProvider(new StubHttpClientFactory(stub), NullLogger<OllamaProvider>.Instance);
        var (ok, err) = await sut.ProbeAsync("http://127.0.0.1:11434", CancellationToken.None);
        Assert.True(ok);
        Assert.Null(err);
        Assert.Equal("http://127.0.0.1:11434/api/tags", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public void DefaultComplianceClass_Is_LocalOnly()
    {
        Assert.Equal(ProviderComplianceClass.LocalOnly, OllamaProvider.DefaultComplianceClass);
        Assert.StartsWith("http://127.0.0.1", OllamaProvider.DefaultEndpoint);
    }
}
