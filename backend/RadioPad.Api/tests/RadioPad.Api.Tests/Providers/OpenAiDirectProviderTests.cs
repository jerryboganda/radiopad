using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class OpenAiDirectProviderTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"OpenAI direct reply."}}],"usage":{"prompt_tokens":12,"completion_tokens":3}}
        """;

    private static AiCompletionRequest Request() =>
        new(new ProviderConfig
        {
            Name = "openai-test",
            Adapter = OpenAiDirectProvider.AdapterId,
            Model = "gpt-4o-mini",
            ApiKeySecretRef = "env:OPENAI_TEST_KEY",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Uses_Bearer_And_Default_Url()
    {
        using var env = EnvVarScope.Set("OPENAI_TEST_KEY", "sk-test");
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiDirectProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiDirectProvider>.Instance);
        var r = await sut.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("OpenAI direct reply.", r.Text);
        Assert.Equal(12, r.InputTokens);
        Assert.Equal(3, r.OutputTokens);
        var req = stub.Captured[0];
        Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", req.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task EndpointEndingInV1_DoesNotDoubleAppendV1()
    {
        using var env = EnvVarScope.Set("OPENAI_TEST_KEY", "sk-test");
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiDirectProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiDirectProvider>.Instance);
        var req = Request();
        req.Provider.EndpointUrl = "https://api.openai.com/v1";

        await sut.CompleteAsync(req, CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        using var env = EnvVarScope.Set("OPENAI_TEST_KEY", "sk-test");
        var stub = StubHandler.Json(HttpStatusCode.TooManyRequests, "{\"error\":\"rate limited\"}");
        var sut = new OpenAiDirectProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiDirectProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(429, ex.StatusCode);
    }

    [Fact]
    public async Task Missing_ApiKey_Throws()
    {
        using var env1 = EnvVarScope.Set("OPENAI_TEST_KEY", null);
        using var env2 = EnvVarScope.Set("OPENAI_API_KEY", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiDirectProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiDirectProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Response_Without_Usage_Returns_Zero()
    {
        using var env = EnvVarScope.Set("OPENAI_TEST_KEY", "sk-test");
        var stub = StubHandler.Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]}");
        var sut = new OpenAiDirectProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiDirectProvider>.Instance);
        var r = await sut.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("hi", r.Text);
        Assert.Equal(0, r.InputTokens + r.OutputTokens);
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
