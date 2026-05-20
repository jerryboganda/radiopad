using RadioPad.Cli.Commands;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests.Cli;

/// <summary>
/// Iter-31 G / CLI-007 — locks the wire shape of the
/// <c>POST /api/providers</c> payload so each adapter remains
/// configurable from the CLI without surprises (see <c>SaveProviderDto</c>).
/// </summary>
public class ProviderRegisterTests
{
    [Fact]
    public void BuildPayload_OpenAi_DefaultsBaseUrl()
    {
        var p = ProviderRegister.BuildPayload(
            type: "openai",
            name: "OpenAI Prod",
            baseUrl: null,
            model: "gpt-4o-mini",
            apiKeyRef: "env:OPENAI_API_KEY");

        Assert.Equal("openai", p["adapter"]);
        Assert.Equal("OpenAI Prod", p["name"]);
        Assert.Equal("gpt-4o-mini", p["model"]);
        Assert.Equal("https://api.openai.com/v1", p["endpointUrl"]);
        Assert.Equal("env:OPENAI_API_KEY", p["apiKeySecretRef"]);
        Assert.Equal((int)ProviderComplianceClass.Sandbox, p["compliance"]);
        Assert.Equal(true, p["enabled"]);
        Assert.Null(p["id"]);
    }

    [Fact]
    public void BuildPayload_Anthropic_DefaultsBaseUrl()
    {
        var p = ProviderRegister.BuildPayload("anthropic", "Anthropic", null, "claude-3-5-sonnet", "env:ANTHROPIC_API_KEY");
        Assert.Equal("anthropic", p["adapter"]);
        Assert.Equal("https://api.anthropic.com", p["endpointUrl"]);
    }

    [Fact]
    public void BuildPayload_Ollama_DefaultsLocalhost()
    {
        var p = ProviderRegister.BuildPayload("ollama", "Local Ollama", null, "llama3", "");
        Assert.Equal("ollama", p["adapter"]);
        Assert.Equal("http://127.0.0.1:11434", p["endpointUrl"]);
    }

    [Fact]
    public void BuildPayload_HonoursExplicitBaseUrl()
    {
        var p = ProviderRegister.BuildPayload(
            type: "azure-openai",
            name: "Azure",
            baseUrl: "https://contoso.openai.azure.com/openai/deployments/gpt-4o",
            model: "gpt-4o",
            apiKeyRef: "env:AZURE_OPENAI_KEY");
        Assert.Equal("azure-openai", p["adapter"]);
        Assert.Equal("https://contoso.openai.azure.com/openai/deployments/gpt-4o", p["endpointUrl"]);
    }

    [Theory]
    [InlineData("gcp-vertex", "google-vertex")]
    [InlineData("google-vertex-ai", "google-vertex")]
    [InlineData("openai-direct", "openai")]
    [InlineData("github-copilot-sdk", "github-copilot-sdk")]
    [InlineData("github-copilot-cli", "github-copilot-cli")]
    [InlineData("gemini-cli", "gemini-cli")]
    [InlineData("codex-cli", "codex-cli")]
    [InlineData("openai-compatible", "openai-compatible")]
    public void BuildPayload_UsesCanonicalAdapterIds(string input, string expected)
    {
        var p = ProviderRegister.BuildPayload(input, "P", null, "m", "");
        Assert.Equal(expected, p["adapter"]);
        Assert.Equal((int)ProviderComplianceClass.Sandbox, p["compliance"]);
    }

    [Fact]
    public void BuildPayload_RejectsUnknownAdapterType()
    {
        Assert.Throws<ArgumentException>(() =>
            ProviderRegister.BuildPayload("not-a-real-adapter", "x", null, "m", "env:K"));
    }

    [Fact]
    public void TemplatesImport_BuildsSavePayload_FromJson()
    {
        var raw = "{\"templateId\":\"chest-ct\",\"name\":\"Chest CT\",\"modality\":\"CT\",\"bodyPart\":\"Chest\",\"sections\":[{\"id\":\"findings\"}]}";
        var dto = TemplatesCommands.BuildSavePayload(raw, ".json");
        Assert.NotNull(dto);
        Assert.Equal("chest-ct", dto!["templateId"]);
        Assert.Equal("Chest CT", dto["name"]);
        Assert.Equal("CT", dto["modality"]);
        Assert.Equal("Chest", dto["bodyPart"]);
        Assert.Contains("findings", dto["sectionsJson"]!.ToString());
    }
}
