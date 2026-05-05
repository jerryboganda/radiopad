using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class AwsBedrockProviderTests
{
    private const string AnthropicOk = """
        {"content":[{"type":"text","text":"Bedrock impression."}],"usage":{"input_tokens":31,"output_tokens":7}}
        """;

    private const string TitanOk = """
        {"results":[{"outputText":"Titan reply.","tokenCount":9}],"inputTextTokenCount":15}
        """;

    private static AiCompletionRequest Request(string model = "anthropic.claude-3-5-sonnet-20241022-v2:0") =>
        new(new ProviderConfig
        {
            Name = "bedrock-test",
            Adapter = AwsBedrockProvider.AdapterId,
            Model = model,
            EndpointUrl = "https://bedrock-runtime.us-west-2.amazonaws.com",
            ApiKeySecretRef = "env:RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    private static IDisposable WithCreds()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID", "AKIAEXAMPLE");
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY", "secret");
        return new DisposableAction(() =>
        {
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID", null);
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY", null);
        });
    }

    [Fact]
    public async Task HappyPath_Anthropic_Parses_Text_And_Usage_And_Signs_Request()
    {
        using var _ = WithCreds();
        var stub = StubHandler.Json(HttpStatusCode.OK, AnthropicOk);
        var sut = new AwsBedrockProvider(new StubHttpClientFactory(stub), NullLogger<AwsBedrockProvider>.Instance);

        var r = await sut.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("Bedrock impression.", r.Text);
        Assert.Equal(31, r.InputTokens);
        Assert.Equal(7, r.OutputTokens);
        var req = stub.Captured[0];
        Assert.Contains("/model/anthropic.claude-3-5-sonnet-20241022-v2%3A0/invoke", req.RequestUri!.AbsolutePath);
        // SigV4 signature applied
        Assert.True(req.Headers.Contains("Authorization"));
        var auth = string.Join(' ', req.Headers.GetValues("Authorization"));
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE/", auth);
        Assert.Contains("us-west-2/bedrock/aws4_request", auth);
        Assert.True(req.Headers.Contains("x-amz-date"));
        Assert.True(req.Headers.Contains("x-amz-content-sha256"));
    }

    [Fact]
    public async Task HappyPath_Titan_Parses_Text_And_Usage()
    {
        using var _ = WithCreds();
        var stub = StubHandler.Json(HttpStatusCode.OK, TitanOk);
        var sut = new AwsBedrockProvider(new StubHttpClientFactory(stub), NullLogger<AwsBedrockProvider>.Instance);

        var r = await sut.CompleteAsync(Request("amazon.titan-text-express-v1"), CancellationToken.None);

        Assert.Equal("Titan reply.", r.Text);
        Assert.Equal(15, r.InputTokens);
        Assert.Equal(9, r.OutputTokens);
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        using var _ = WithCreds();
        var stub = StubHandler.Json(HttpStatusCode.Forbidden, "{\"message\":\"AccessDenied\"}");
        var sut = new AwsBedrockProvider(new StubHttpClientFactory(stub), NullLogger<AwsBedrockProvider>.Instance);
        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Missing_Credentials_Throws()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_ACCESS_KEY_ID", null);
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_AWS_SECRET_ACCESS_KEY", null);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", null);
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, AnthropicOk);
        var sut = new AwsBedrockProvider(new StubHttpClientFactory(stub), NullLogger<AwsBedrockProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _onDispose;
        public DisposableAction(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
