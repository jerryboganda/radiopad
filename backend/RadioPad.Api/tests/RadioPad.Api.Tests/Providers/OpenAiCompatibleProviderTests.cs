using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class OpenAiCompatibleProviderTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"Compat reply."}}],"usage":{"prompt_tokens":5,"completion_tokens":2}}
        """;

    private static AiCompletionRequest Request(string baseUrl, string keyEnv = "COMPAT_KEY") =>
        new(new ProviderConfig
        {
            Name = "compat-test",
            Adapter = OpenAiCompatibleProvider.AdapterId,
            Model = "llama-3.1-70b-instruct",
            EndpointUrl = baseUrl,
            ApiKeySecretRef = "env:" + keyEnv,
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Appends_V1ChatCompletions_When_Missing()
    {
        using var env = EnvVarScope.Set("COMPAT_KEY", "k1");
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        var r = await sut.CompleteAsync(Request("https://api.together.xyz"), CancellationToken.None);

        Assert.Equal("Compat reply.", r.Text);
        Assert.Equal(5, r.InputTokens);
        Assert.Equal(2, r.OutputTokens);
        Assert.Equal("https://api.together.xyz/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
        Assert.Equal("k1", stub.Captured[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task FullUrl_With_ChatCompletions_Path_Is_Used_Verbatim()
    {
        using var env = EnvVarScope.Set("COMPAT_KEY", "k1");
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        await sut.CompleteAsync(Request("https://example.com/custom/v1/chat/completions"), CancellationToken.None);
        Assert.Equal("https://example.com/custom/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task EndpointEndingInV1_DoesNotDoubleAppendV1()
    {
        using var env = EnvVarScope.Set("COMPAT_KEY", "k1");
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        await sut.CompleteAsync(Request("https://api.together.xyz/v1"), CancellationToken.None);
        Assert.Equal("https://api.together.xyz/v1/chat/completions", stub.Captured[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        using var env = EnvVarScope.Set("COMPAT_KEY", "k1");
        var stub = StubHandler.Json(HttpStatusCode.NotFound, "{\"error\":\"missing\"}");
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request("https://api.together.xyz"), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Missing_EndpointUrl_Throws()
    {
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new OpenAiCompatibleProvider(new StubHttpClientFactory(stub), NullLogger<OpenAiCompatibleProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(""), CancellationToken.None));
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434", ProviderComplianceClass.LocalOnly)]
    [InlineData("http://localhost:8000", ProviderComplianceClass.LocalOnly)]
    [InlineData("http://gpu-host.local", ProviderComplianceClass.LocalOnly)]
    [InlineData("https://api.together.xyz", ProviderComplianceClass.Sandbox)]
    [InlineData("https://api.groq.com", ProviderComplianceClass.Sandbox)]
    [InlineData("", ProviderComplianceClass.Sandbox)]
    public void ResolveDefaultComplianceClass_Maps_Hosts_Correctly(string url, ProviderComplianceClass expected)
    {
        Assert.Equal(expected, OpenAiCompatibleProvider.ResolveDefaultComplianceClass(url));
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
