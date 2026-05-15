using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Services;
using RadioPad.Application.Services.Pacs;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Pacs;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

// =====================================================================
// A6-1 — Provider API endpoint secret-ref validation (needs the full
//         WebApplicationFactory pipeline).
// =====================================================================

/// <summary>
/// Validates the <c>POST /api/providers</c> endpoint rejects inline API
/// keys and accepts <c>env:NAME</c> references.
/// </summary>
public class SecretHandlingEndpointTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public SecretHandlingEndpointTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Provider_Save_Rejects_Inline_ApiKey()
    {
        using var client = _factory.CreateAdminClient();
        var resp = await client.PostAsJsonAsync("/api/providers", new
        {
            name = "inline-secret-test",
            adapter = "mock",
            model = "",
            endpointUrl = "",
            apiKeySecretRef = "sk-SOME_RAW_KEY_VALUE_1234567890",
            compliance = (int)ProviderComplianceClass.Sandbox,
            enabled = true,
            priority = 100,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var error = doc.RootElement.GetProperty("error").GetString();
        Assert.NotNull(error);
        Assert.Contains("env:", error!);
    }

    [Fact]
    public async Task Provider_Save_Accepts_EnvRef_ApiKey()
    {
        using var client = _factory.CreateAdminClient();
        var resp = await client.PostAsJsonAsync("/api/providers", new
        {
            name = $"env-ref-test-{Guid.NewGuid():N}",
            adapter = "mock",
            model = "",
            endpointUrl = "",
            apiKeySecretRef = "env:TEST_PROVIDER_KEY",
            compliance = (int)ProviderComplianceClass.Sandbox,
            enabled = true,
            priority = 100,
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("id", out _));
    }
}

// =====================================================================
// A6-2..4 — Unit-level secret-handling tests. These exercise provider
//            adapters and PACS secret resolution without needing the
//            full HTTP host, so they run fast and in any CI shape.
// =====================================================================

/// <summary>
/// Verifies env-ref resolution at runtime, graceful handling of missing
/// env vars, and that PACS vendor adapters only resolve secrets from env
/// variables (never inline literals).
/// </summary>
public class SecretHandlingTests
{
    // -----------------------------------------------------------------
    // 2. Provider env-ref resolution at runtime
    // -----------------------------------------------------------------

    [Fact]
    public async Task Provider_EnvRef_Resolves_Env_Var_At_Runtime()
    {
        using var env = EnvVarScope.Set("SECRET_HANDLING_TEST_KEY", "test-api-key-42");

        var stub = Providers.StubHandler.Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""");
        var sut = new RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider(
            new Providers.StubHttpClientFactory(stub),
            NullLogger<RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider>.Instance);

        var req = new RadioPad.Application.Abstractions.AiCompletionRequest(
            new ProviderConfig
            {
                Name = "env-resolve-test",
                Adapter = RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider.AdapterId,
                Model = "test-model",
                EndpointUrl = "https://api.example.com",
                ApiKeySecretRef = "env:SECRET_HANDLING_TEST_KEY",
                Compliance = ProviderComplianceClass.Sandbox,
                Enabled = true,
            }, "sys", "user", "v1", ContainsPhi: false);

        var result = await sut.CompleteAsync(req, CancellationToken.None);
        Assert.Equal("ok", result.Text);

        // Verify the stub captured an Authorization header with the key
        Assert.Single(stub.Captured);
        var captured = stub.Captured[0];
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("test-api-key-42", captured.Headers.Authorization?.Parameter);
    }

    // -----------------------------------------------------------------
    // 3. Provider empty env-ref (nonexistent var)
    // -----------------------------------------------------------------

    [Fact]
    public async Task Provider_EnvRef_Nonexistent_Var_ThrowsPolicy_NotCrash()
    {
        using var env = EnvVarScope.Set("SECRET_HANDLING_NONEXISTENT_VAR", null);

        var stub = Providers.StubHandler.Json(HttpStatusCode.OK, "{}");
        var sut = new RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider(
            new Providers.StubHttpClientFactory(stub),
            NullLogger<RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider>.Instance);

        var req = new RadioPad.Application.Abstractions.AiCompletionRequest(
            new ProviderConfig
            {
                Name = "missing-env-test",
                Adapter = RadioPad.Infrastructure.Providers.OpenAiCompatibleProvider.AdapterId,
                Model = "test-model",
                EndpointUrl = "https://api.example.com",
                ApiKeySecretRef = "env:SECRET_HANDLING_NONEXISTENT_VAR",
                Compliance = ProviderComplianceClass.Sandbox,
                Enabled = true,
            }, "sys", "user", "v1", ContainsPhi: false);

        // Should throw a policy exception (api_key_missing), not an
        // unhandled NullReferenceException or similar crash.
        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(
            () => sut.CompleteAsync(req, CancellationToken.None));
        Assert.Contains("api_key_missing", ex.Message);
    }

    // -----------------------------------------------------------------
    // 4. PACS secret env-only — inline values not accepted
    // -----------------------------------------------------------------

    [Fact]
    public async Task Pacs_SecretResolver_Resolves_EnvRef()
    {
        using var baseEnv = EnvVarScope.Set("RADIOPAD_PACS_SECTRA_BASE", "https://sectra.test");
        using var tokenEnv = EnvVarScope.Set("RADIOPAD_PACS_SECTRA_TOKEN", "pacs-token-abc");

        var handler = new StubPacsHandler(HttpStatusCode.OK, "{}");
        var adapter = new SectraIds7Adapter(
            new StubPacsFactory(handler),
            NullLogger<SectraIds7Adapter>.Instance);

        var health = await adapter.ProbeAsync(CancellationToken.None);
        Assert.Equal(PacsAdapterHealthStatus.Healthy, health.Status);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("pacs-token-abc", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Pacs_SecretResolver_InlineValue_FallsBack_To_Env()
    {
        // When RADIOPAD_PACS_SECTRA_TOKEN_REF is set to a literal (not
        // env:NAME), PacsSecretResolver falls back to the
        // RADIOPAD_PACS_SECTRA_TOKEN env var — inline secrets are NOT
        // forwarded. This verifies the env-only contract.
        using var baseEnv = EnvVarScope.Set("RADIOPAD_PACS_SECTRA_BASE", "https://sectra.test");
        using var refEnv = EnvVarScope.Set("RADIOPAD_PACS_SECTRA_TOKEN_REF", "inline-literal-token");
        using var tokenEnv = EnvVarScope.Set("RADIOPAD_PACS_SECTRA_TOKEN", "env-only-token");

        var handler = new StubPacsHandler(HttpStatusCode.OK, "{}");
        var adapter = new SectraIds7Adapter(
            new StubPacsFactory(handler),
            NullLogger<SectraIds7Adapter>.Instance);

        var health = await adapter.ProbeAsync(CancellationToken.None);
        Assert.Equal(PacsAdapterHealthStatus.Healthy, health.Status);

        Assert.NotNull(handler.LastRequest);
        var authParam = handler.LastRequest!.Headers.Authorization?.Parameter;
        Assert.Equal("env-only-token", authParam);
    }

    // ----- Minimal stubs for PACS adapter tests -----

    private sealed class StubPacsHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubPacsHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new System.Net.Http.StringContent(
                    _body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubPacsFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubPacsFactory(HttpMessageHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}
