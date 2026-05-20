using RadioPad.Application.Services;
using RadioPad.Infrastructure.Providers;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

public class ProviderEndpointPolicyTests
{
    [Theory]
    [InlineData(OpenAiDirectProvider.AdapterId, "https://evil.example/v1")]
    [InlineData(OpenAiDirectProvider.AdapterId, "https://user:pass@api.openai.com/v1")]
    [InlineData(OpenAiDirectProvider.AdapterId, "http://api.openai.com/v1")]
    [InlineData(AzureOpenAiProvider.AdapterId, "https://aoai.example.com")]
    [InlineData(AwsBedrockProvider.AdapterId, "https://bedrock-runtime.us-east-1.evil.example")]
    [InlineData(GoogleVertexAiProvider.AdapterId, "https://metadata.google.internal")]
    public void HostedEndpointPolicy_Rejects_Unsafe_Overrides(string adapterId, string endpoint)
    {
        var ex = Assert.Throws<ProviderPolicyException>(() => OpenAiChatHelpers.ValidateHostedEndpoint(endpoint, adapterId));
        Assert.Contains(adapterId, ex.Message);
    }

    [Theory]
    [InlineData(OpenAiDirectProvider.AdapterId, "https://api.openai.com/v1")]
    [InlineData(AzureOpenAiProvider.AdapterId, "https://contoso.openai.azure.com?api-version=2025-03-01")]
    [InlineData(AwsBedrockProvider.AdapterId, "https://bedrock-runtime.us-east-1.amazonaws.com")]
    [InlineData(GoogleVertexAiProvider.AdapterId, "https://us-central1-aiplatform.googleapis.com")]
    public void HostedEndpointPolicy_Allows_Expected_Vendor_Hosts(string adapterId, string endpoint)
    {
        var uri = OpenAiChatHelpers.ValidateHostedEndpoint(endpoint, adapterId);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }
}