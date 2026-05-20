using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class AzureOpenAiProviderTests
{
    private const string OkBody = """
        {"choices":[{"message":{"role":"assistant","content":"Impression: no acute findings."}}],"usage":{"prompt_tokens":42,"completion_tokens":11}}
        """;

    private static AiCompletionRequest Request(string apiKeyEnv = "AZ_KEY") =>
        new(new ProviderConfig
        {
            Name = "azure-test",
            Adapter = AzureOpenAiProvider.AdapterId,
            Model = "gpt4o-deployment",
            EndpointUrl = "https://example-aoai.openai.azure.com",
            ApiKeySecretRef = "env:" + apiKeyEnv,
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Parses_Text_And_Usage_And_Sends_ApiKey_Header()
    {
        Environment.SetEnvironmentVariable("AZ_KEY", "secret-value");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
            var sut = new AzureOpenAiProvider(new StubHttpClientFactory(stub), NullLogger<AzureOpenAiProvider>.Instance);

            var result = await sut.CompleteAsync(Request(), CancellationToken.None);

            Assert.Equal("Impression: no acute findings.", result.Text);
            Assert.Equal(42, result.InputTokens);
            Assert.Equal(11, result.OutputTokens);
            Assert.Equal("gpt4o-deployment", result.Model);
            Assert.Single(stub.Captured);
            var req = stub.Captured[0];
            Assert.Contains("openai/deployments/gpt4o-deployment/chat/completions", req.RequestUri!.ToString());
            Assert.Contains("api-version=2024-08-01-preview", req.RequestUri!.Query);
            Assert.Equal("secret-value", req.Headers.GetValues("api-key").Single());
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZ_KEY", null);
        }
    }

    [Fact]
    public async Task EndpointWith_ApiVersion_Query_Is_Honoured()
    {
        Environment.SetEnvironmentVariable("AZ_KEY", "k");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
            var sut = new AzureOpenAiProvider(new StubHttpClientFactory(stub), NullLogger<AzureOpenAiProvider>.Instance);
            var req = Request();
            req.Provider.EndpointUrl = "https://example-aoai.openai.azure.com?api-version=2025-03-01";
            await sut.CompleteAsync(req, CancellationToken.None);
            Assert.Contains("api-version=2025-03-01", stub.Captured[0].RequestUri!.Query);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZ_KEY", null);
        }
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException_With_Status()
    {
        Environment.SetEnvironmentVariable("AZ_KEY", "k");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.BadGateway, "{\"error\":\"upstream\"}");
            var sut = new AzureOpenAiProvider(new StubHttpClientFactory(stub), NullLogger<AzureOpenAiProvider>.Instance);
            var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
            Assert.Equal(502, ex.StatusCode);
            Assert.Contains("upstream", ex.ResponseBody);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZ_KEY", null);
        }
    }

    [Fact]
    public async Task Missing_ApiKey_Throws()
    {
        Environment.SetEnvironmentVariable("AZ_KEY", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new AzureOpenAiProvider(new StubHttpClientFactory(stub), NullLogger<AzureOpenAiProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request("AZ_KEY"), CancellationToken.None));
    }

    [Fact]
    public async Task Response_Without_Usage_Returns_ZeroTokens()
    {
        Environment.SetEnvironmentVariable("AZ_KEY", "k");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hi\"}}]}");
            var sut = new AzureOpenAiProvider(new StubHttpClientFactory(stub), NullLogger<AzureOpenAiProvider>.Instance);
            var r = await sut.CompleteAsync(Request(), CancellationToken.None);
            Assert.Equal("hi", r.Text);
            Assert.Equal(0, r.InputTokens);
            Assert.Equal(0, r.OutputTokens);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZ_KEY", null);
        }
    }
}
