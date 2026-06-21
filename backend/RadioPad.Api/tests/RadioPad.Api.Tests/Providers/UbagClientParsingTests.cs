using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Infrastructure.Providers.Ubag;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="UbagClient"/> JSON parsing.
/// Uses a stub <see cref="HttpMessageHandler"/> wired to a named
/// <see cref="IHttpClientFactory"/> — no network required.
/// </summary>
public class UbagClientParsingTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="UbagClient"/> whose HTTP calls are answered by
    /// <paramref name="handler"/>. Sets a BaseAddress so relative paths resolve.
    /// </summary>
    private static UbagClient BuildClient(StubHandler handler)
    {
        var factory = new BaseAddressStubHttpClientFactory(handler, "http://localhost/");
        return new UbagClient(factory, NullLogger<UbagClient>.Instance);
    }

    // ── /v1/targets — real gateway shape ───────────────────────────────────────

    private const string RealTargetsJson = """
        {
          "api_version": "2026-05-22",
          "kind": "targets",
          "data": [
            {"adapter_key":"mock","display_name":"Mock Target","key":"mock","manual_login_required":false,"safe_mode":true},
            {"adapter_key":"deepseek_web","display_name":"DeepSeek Web","key":"deepseek_web","manual_login_required":true,"safe_mode":true},
            {"adapter_key":"gemini_web","display_name":"Gemini Web","key":"gemini_web","manual_login_required":true,"safe_mode":true}
          ],
          "next_cursor": null,
          "trace_id": "trace_abc"
        }
        """;

    [Fact]
    public async Task ListTargetsAsync_RealShape_Returns3Targets()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, RealTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);
        Assert.Equal(3, targets.Count);
    }

    [Fact]
    public async Task ListTargetsAsync_RealShape_IdsAreKeyField()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, RealTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        Assert.Equal("mock", targets[0].Id);
        Assert.Equal("deepseek_web", targets[1].Id);
        Assert.Equal("gemini_web", targets[2].Id);
    }

    [Fact]
    public async Task ListTargetsAsync_RealShape_DisplayNamesCorrect()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, RealTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        Assert.Equal("Mock Target", targets[0].Name);
        Assert.Equal("DeepSeek Web", targets[1].Name);
        Assert.Equal("Gemini Web", targets[2].Name);
    }

    [Fact]
    public async Task ListTargetsAsync_RealShape_StatusIsListed_WhenNoStatusField()
    {
        // The real gateway has no status/state field on targets —
        // the client should record "listed" as a neutral placeholder.
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, RealTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        Assert.All(targets, t => Assert.Equal("listed", t.Status));
    }

    [Fact]
    public async Task ListTargetsAsync_RealShape_ReadyIsFalse_ForAllTargets()
    {
        // The real /v1/targets shape carries no readiness field and no ok-status value —
        // every target comes back Ready=false. True readiness is derived separately by
        // cross-referencing /v1/browser/contexts via MergeTargetReadiness.
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, RealTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        Assert.All(targets, t => Assert.False(t.Ready));
    }

    // ── /v1/targets — legacy shapes (backward-compat) ─────────────────────────

    private const string LegacyTargetsJson = """
        {
          "targets": [
            {"id":"legacy_target","name":"Legacy Target","status":"ready","ready":true},
            {"id":"offline_target","name":"Offline","status":"offline"}
          ]
        }
        """;

    [Fact]
    public async Task ListTargetsAsync_LegacyShape_ParsesTargetsProperty()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, LegacyTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        Assert.Equal(2, targets.Count);
        Assert.Equal("legacy_target", targets[0].Id);
        Assert.Equal("Legacy Target", targets[0].Name);
        Assert.True(targets[0].Ready);
    }

    [Fact]
    public async Task ListTargetsAsync_LegacyShape_ReadyFalse_WhenStatusNotOk()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, LegacyTargetsJson));
        var targets = await sut.ListTargetsAsync(CancellationToken.None);

        // "offline" status → Ready should be false (not a recognised ok status)
        Assert.False(targets[1].Ready);
    }

    // ── /v1/browser/contexts ───────────────────────────────────────────────────

    private const string ContextsJson = """
        {
          "api_version": "2026-05-22",
          "kind": "provider_contexts",
          "data": [
            {"context_id":"ctx_prod_deepseek","target_id":"deepseek_web","login_state":"authenticated","instance_id":"br_prod_browser_viewer","tenant_id":"tenant_edge"},
            {"context_id":"ctx_prod_gemini","target_id":"gemini_web","login_state":"authenticated","instance_id":"br_prod_browser_viewer","tenant_id":"tenant_edge"},
            {"context_id":"ctx_prod_chatgpt","target_id":"chatgpt_web","login_state":"unknown","instance_id":"br_prod_browser_viewer","tenant_id":"tenant_edge"}
          ],
          "next_cursor": null,
          "trace_id": "trace_def"
        }
        """;

    [Fact]
    public async Task ListBrowserContextsAsync_Returns3Contexts()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, ContextsJson));
        var contexts = await sut.ListBrowserContextsAsync(CancellationToken.None);
        Assert.Equal(3, contexts.Count);
    }

    [Fact]
    public async Task ListBrowserContextsAsync_DeepSeek_IsAuthenticated()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, ContextsJson));
        var contexts = await sut.ListBrowserContextsAsync(CancellationToken.None);
        var ctx = Assert.Single(contexts, c => c.TargetId == "deepseek_web");
        Assert.True(ctx.Authenticated);
    }

    [Fact]
    public async Task ListBrowserContextsAsync_Gemini_IsAuthenticated()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, ContextsJson));
        var contexts = await sut.ListBrowserContextsAsync(CancellationToken.None);
        var ctx = Assert.Single(contexts, c => c.TargetId == "gemini_web");
        Assert.True(ctx.Authenticated);
    }

    [Fact]
    public async Task ListBrowserContextsAsync_ChatGpt_IsNotAuthenticated()
    {
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, ContextsJson));
        var contexts = await sut.ListBrowserContextsAsync(CancellationToken.None);
        var ctx = Assert.Single(contexts, c => c.TargetId == "chatgpt_web");
        Assert.False(ctx.Authenticated);
        Assert.Equal("unknown", ctx.LoginState);
    }

    [Fact]
    public async Task ListBrowserContextsAsync_SkipsRowsWithEmptyTargetId()
    {
        const string json = """
            {"data":[
              {"context_id":"ctx_a","target_id":"","login_state":"authenticated"},
              {"context_id":"ctx_b","login_state":"authenticated"},
              {"context_id":"ctx_c","target_id":"real_target","login_state":"authenticated"}
            ]}
            """;
        var sut = BuildClient(StubHandler.Json(HttpStatusCode.OK, json));
        var contexts = await sut.ListBrowserContextsAsync(CancellationToken.None);

        // Only the row with a non-empty target_id should survive
        Assert.Single(contexts);
        Assert.Equal("real_target", contexts[0].TargetId);
    }

    // ── helper factory ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <see cref="StubHttpClientFactory"/> and adds a <c>BaseAddress</c>
    /// so <see cref="UbagClient"/>'s relative-path requests resolve correctly.
    /// </summary>
    private sealed class BaseAddressStubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly string _baseAddress;

        public BaseAddressStubHttpClientFactory(HttpMessageHandler handler, string baseAddress)
        {
            _handler = handler;
            _baseAddress = baseAddress;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri(_baseAddress),
            };
            return client;
        }
    }
}
