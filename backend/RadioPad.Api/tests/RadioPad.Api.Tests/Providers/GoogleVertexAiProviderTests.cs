using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class GoogleVertexAiProviderTests
{
    private const string OkBody = """
        {"candidates":[{"content":{"parts":[{"text":"Vertex impression."}]}}],"usageMetadata":{"promptTokenCount":17,"candidatesTokenCount":4}}
        """;

    private static AiCompletionRequest Request() =>
        new(new ProviderConfig
        {
            Name = "vertex-test",
            Adapter = GoogleVertexAiProvider.AdapterId,
            Model = "gemini-1.5-pro",
            EndpointUrl = "https://us-central1-aiplatform.googleapis.com",
            ApiKeySecretRef = "",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        }, "sys", "user", "v1", ContainsPhi: false);

    [Fact]
    public async Task HappyPath_Uses_Bearer_Token_And_Parses_Response()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", "ya29.example");
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID", "demo-proj");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
            var sut = new GoogleVertexAiProvider(new StubHttpClientFactory(stub), NullLogger<GoogleVertexAiProvider>.Instance);

            var r = await sut.CompleteAsync(Request(), CancellationToken.None);

            Assert.Equal("Vertex impression.", r.Text);
            Assert.Equal(17, r.InputTokens);
            Assert.Equal(4, r.OutputTokens);
            var req = stub.Captured[0];
            Assert.Contains("/v1/projects/demo-proj/locations/us-central1/publishers/google/models/gemini-1.5-pro:generateContent", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("ya29.example", req.Headers.Authorization!.Parameter);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", null);
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID", null);
        }
    }

    [Fact]
    public async Task UpstreamError_Throws_ProviderTransportException()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", "tok");
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID", "demo-proj");
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.InternalServerError, "{\"error\":\"oops\"}");
            var sut = new GoogleVertexAiProvider(new StubHttpClientFactory(stub), NullLogger<GoogleVertexAiProvider>.Instance);
            var ex = await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
            Assert.Equal(500, ex.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", null);
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID", null);
        }
    }

    [Fact]
    public async Task Missing_Project_Id_Throws()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", "tok");
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_PROJECT_ID", null);
        try
        {
            var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
            var sut = new GoogleVertexAiProvider(new StubHttpClientFactory(stub), NullLogger<GoogleVertexAiProvider>.Instance);
            await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", null);
        }
    }

    [Fact]
    public async Task Missing_Token_And_No_ServiceAccount_Throws()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_ACCESS_TOKEN", null);
        Environment.SetEnvironmentVariable("RADIOPAD_PROVIDER_GCP_SERVICE_ACCOUNT_JSON", null);
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS_JSON", null);
        var stub = StubHandler.Json(HttpStatusCode.OK, OkBody);
        var sut = new GoogleVertexAiProvider(new StubHttpClientFactory(stub), NullLogger<GoogleVertexAiProvider>.Instance);
        await Assert.ThrowsAsync<ProviderTransportException>(() => sut.CompleteAsync(Request(), CancellationToken.None));
    }
}
