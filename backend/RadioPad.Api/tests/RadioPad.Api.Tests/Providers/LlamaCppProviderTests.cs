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

    /// <summary>
    /// Report-generation callers (AiGateway → ProviderRouter) hit this adapter directly with no
    /// wrapper to translate a bare transport failure — unlike the dictation formatter, which already
    /// gets an actionable message from LocalMedGemmaFormatter.DiagnoseUnreachable. A "Connection
    /// refused" against OUR managed default endpoint must now name the missing link (model not
    /// downloaded here, since the test environment never has the 2.5 GB GGUF installed) rather than
    /// surfacing the raw exception message verbatim to the radiologist.
    /// </summary>
    [Fact]
    public async Task ConnectionFailure_Against_Managed_Endpoint_Names_The_Missing_Model()
    {
        var stub = new StubHandler { Responder = (_, _) => throw new HttpRequestException("Connection refused") };
        var server = new LlamaServerProcess(NullLogger<LlamaServerProcess>.Instance);
        var catalog = new LocalModelCatalog();
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance, server, catalog);

        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(
            new AiCompletionRequest(new ProviderConfig
            {
                Name = "local-medgemma",
                Adapter = LlamaCppProvider.AdapterId,
                Model = LocalModelCatalog.MedGemmaId,
                EndpointUrl = "http://127.0.0.1:8080",
                Compliance = ProviderComplianceClass.LocalOnly,
                Enabled = true,
            }, "sys", "user", "v1", ContainsPhi: false),
            CancellationToken.None));

        Assert.Contains("not downloaded", ex.Message);
        Assert.Contains("On-device models", ex.Message);
    }

    /// <summary>An operator's own remote EndpointUrl must get the raw message unchanged — RadioPad
    /// never tried to manage that server, so a diagnosis about OUR model store would be misleading.</summary>
    [Fact]
    public async Task ConnectionFailure_Against_A_Custom_Endpoint_Is_Not_Diagnosed()
    {
        var stub = new StubHandler { Responder = (_, _) => throw new HttpRequestException("Connection refused") };
        var sut = new LlamaCppProvider(new StubHttpClientFactory(stub), NullLogger<LlamaCppProvider>.Instance);

        var ex = await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request("http://10.0.0.5:9999"), CancellationToken.None));

        Assert.DoesNotContain("On-device models", ex.Message);
    }
}
