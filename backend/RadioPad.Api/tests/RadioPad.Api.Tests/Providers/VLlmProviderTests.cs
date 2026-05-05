using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class VLlmProviderTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"vllm reply"}}],"usage":{"prompt_tokens":10,"completion_tokens":4}}
        """;

    private static AiCompletionRequest Request(string baseUrl) =>
        new(new ProviderConfig
        {
            Name = "vllm-local",
            Adapter = VLlmProvider.AdapterId,
            Model = "llama-3.1-70b",
            EndpointUrl = baseUrl,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Posts_To_V1ChatCompletions()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        var r = await sut.CompleteAsync(Request("http://127.0.0.1:8000"), CancellationToken.None);
        Assert.Equal("vllm reply", r.Text);
        Assert.Equal(10, r.InputTokens);
        Assert.Equal(4, r.OutputTokens);
        Assert.Equal("http://127.0.0.1:8000/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DefaultEndpoint_Is_Localhost()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        await sut.CompleteAsync(Request(""), CancellationToken.None);
        Assert.Equal("http://127.0.0.1:8000/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task EndpointEndingInV1_DoesNotDoubleAppendV1()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        await sut.CompleteAsync(Request("http://127.0.0.1:8000/v1"), CancellationToken.None);
        Assert.Equal("http://127.0.0.1:8000/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Probe_Targets_V1Models()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, "{\"data\":[]}");
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        var (ok, err) = await sut.ProbeAsync("http://127.0.0.1:8000", CancellationToken.None);
        Assert.True(ok);
        Assert.Equal("http://127.0.0.1:8000/v1/models", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        var stub = StubHandler.Json(HttpStatusCode.InternalServerError, "boom");
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(
            () => sut.CompleteAsync(Request("http://127.0.0.1:8000"), CancellationToken.None));
    }

    [Fact]
    public async Task MissingConfiguredApiKey_ThrowsPolicyException()
    {
        using var env = EnvVarScope.Set("VLLM_MISSING_KEY", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new VLlmProvider(new StubHttpClientFactory(stub), NullLogger<VLlmProvider>.Instance);
        var req = Request("http://127.0.0.1:8000");
        req.Provider.ApiKeySecretRef = "env:VLLM_MISSING_KEY";

        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Contains("api_key_missing", ex.Message);
        Assert.Empty(stub.Captured);
    }

    [Fact]
    public void DefaultComplianceClass_Is_LocalOnly()
    {
        Assert.Equal(ProviderComplianceClass.LocalOnly, VLlmProvider.DefaultComplianceClass);
        Assert.StartsWith("http://127.0.0.1", VLlmProvider.DefaultEndpoint);
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        private EnvVarScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static EnvVarScope Set(string name, string? value) => new(name, value);

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
