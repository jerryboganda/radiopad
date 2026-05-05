using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class LlamaCppProviderTests
{
    private const string OkBody = """
        {"content":"llamacpp reply","tokens_evaluated":11,"tokens_predicted":6}
        """;

    private static AiCompletionRequest Request(string baseUrl) =>
        new(new ProviderConfig
        {
            Name = "llamacpp-local",
            Adapter = LlamaCppProvider.AdapterId,
            Model = "qwen-2.5",
            EndpointUrl = baseUrl,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Posts_To_Completion_And_Parses_Counts()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        var r = await sut.CompleteAsync(Request("http://127.0.0.1:8080"), CancellationToken.None);
        Assert.Equal("llamacpp reply", r.Text);
        Assert.Equal(11, r.InputTokens);
        Assert.Equal(6, r.OutputTokens);
        Assert.Equal("http://127.0.0.1:8080/completion", stub.Captured[0].RequestUri!.ToString());
        Assert.Contains("SYSTEM:", stub.CapturedBodies[0]);
        Assert.Contains("USER:", stub.CapturedBodies[0]);
    }

    [Fact]
    public async Task DefaultEndpoint_Is_Localhost()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        await sut.CompleteAsync(Request(""), CancellationToken.None);
        Assert.Equal("http://127.0.0.1:8080/completion", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Probe_Targets_Health()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        var (ok, err) = await sut.ProbeAsync("http://127.0.0.1:8080", CancellationToken.None);
        Assert.True(ok);
        Assert.Equal("http://127.0.0.1:8080/health", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        var stub = StubHandler.Json(HttpStatusCode.BadGateway, "");
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request("http://127.0.0.1:8080"), CancellationToken.None));
    }

    [Fact]
    public void DefaultComplianceClass_Is_LocalOnly()
    {
        Assert.Equal(ProviderComplianceClass.LocalOnly, LlamaCppProvider.DefaultComplianceClass);
        Assert.StartsWith("http://127.0.0.1", LlamaCppProvider.DefaultEndpoint);
    }
}
