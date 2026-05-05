using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 MCP-007 — capability-scoped registry semantics: deny-by-default,
/// explicit allow tuples, revoke clears, snapshot reflects state.
/// </summary>
public class McpCapabilityRegistryTests
{
    [Fact]
    public void EmptyRegistry_DeniesEverything()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        Assert.False(reg.IsAllowed("any-plugin", "rulebook.lookup"));
        var ex = Assert.Throws<PluginPolicyException>(() =>
            reg.EnsureAllowed("any-plugin", "rulebook.lookup"));
        Assert.Equal("capability_not_registered", ex.Reason);
    }

    [Fact]
    public void Allow_PermitsRegisteredTuple_AndDeniesOthers()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        reg.Allow("acme-pacs", "rulebook.lookup");

        Assert.True(reg.IsAllowed("acme-pacs", "rulebook.lookup"));
        // wrong capability
        Assert.False(reg.IsAllowed("acme-pacs", "report.sign"));
        // wrong plugin
        Assert.False(reg.IsAllowed("other-plugin", "rulebook.lookup"));

        // EnsureAllowed: positive path is silent, negative throws.
        reg.EnsureAllowed("acme-pacs", "rulebook.lookup");
        var denied = Assert.Throws<PluginPolicyException>(() =>
            reg.EnsureAllowed("acme-pacs", "report.sign"));
        Assert.Equal("acme-pacs", denied.PluginId);
    }

    [Fact]
    public void AllowAll_RegistersBatch()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        reg.AllowAll("acme-pacs", new[]
        {
            "dicomweb.read",
            "report.draft.suggest",
            "rulebook.lookup",
        });

        Assert.True(reg.IsAllowed("acme-pacs", "dicomweb.read"));
        Assert.True(reg.IsAllowed("acme-pacs", "report.draft.suggest"));
        Assert.True(reg.IsAllowed("acme-pacs", "rulebook.lookup"));
        Assert.False(reg.IsAllowed("acme-pacs", "report.sign"));
    }

    [Fact]
    public void Revoke_ClearsAllCapabilitiesForPlugin()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        reg.Allow("acme-pacs", "rulebook.lookup");
        reg.Allow("acme-pacs", "dicomweb.read");

        reg.Revoke("acme-pacs");

        Assert.False(reg.IsAllowed("acme-pacs", "rulebook.lookup"));
        Assert.False(reg.IsAllowed("acme-pacs", "dicomweb.read"));
    }

    [Fact]
    public void Snapshot_ReflectsCurrentState()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        reg.Allow("acme-pacs", "rulebook.lookup");
        reg.Allow("acme-pacs", "dicomweb.read");
        reg.Allow("other", "report.draft.suggest");

        var snap = reg.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Contains("dicomweb.read", snap["acme-pacs"]);
        Assert.Contains("rulebook.lookup", snap["acme-pacs"]);
        Assert.Contains("report.draft.suggest", snap["other"]);
    }

    [Fact]
    public void Allow_RejectsBlankInput()
    {
        IMcpCapabilityRegistry reg = new InMemoryMcpCapabilityRegistry();
        Assert.Throws<ArgumentException>(() => reg.Allow("", "x"));
        Assert.Throws<ArgumentException>(() => reg.Allow("x", ""));
    }
}
