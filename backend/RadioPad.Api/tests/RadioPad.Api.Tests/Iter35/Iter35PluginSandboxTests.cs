using System.Runtime.InteropServices;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Mcp;
using Xunit;

namespace RadioPad.Api.Tests.Iter35;

/// <summary>
/// Iter-35 — unit tests for the per-OS plugin sandbox launch builders. We
/// don't actually spawn the sandboxed child here — the tests assert on the
/// <c>ProcessStartInfo</c> the builder returns (executable name, argument
/// list, env tags) so the contract documented in
/// [`desktop/PLUGIN_TRUST.md`](../../../desktop/PLUGIN_TRUST.md) stays
/// honoured. The builders are platform-independent (the OS guard lives on
/// <c>RunAsync</c>) so we can exercise them on the CI host even when the
/// underlying OS isn't Linux/macOS.
/// </summary>
public class Iter35PluginSandboxTests
{
    private static PluginManifest Manifest() => new(
        Id: "demo-plugin",
        Version: "0.1.0",
        Executable: "/usr/local/bin/demo-plugin",
        Capabilities: new[] { "rulebook.lookup" });

    [Fact]
    public void LinuxBuildLaunch_PrefersBwrapWhenOnPath_OtherwiseFallsBackToUnshare()
    {
        var psi = LinuxNamespaceSandbox.BuildLaunch(Manifest(), new[] { "--once" });

        Assert.Contains("RADIOPAD_PLUGIN_ID", psi.Environment.Keys);
        Assert.Equal("demo-plugin", psi.Environment["RADIOPAD_PLUGIN_ID"]);
        Assert.Contains("RADIOPAD_PLUGIN_SANDBOX", psi.Environment.Keys);

        var tag = psi.Environment["RADIOPAD_PLUGIN_SANDBOX"];
        if (tag == "bwrap")
        {
            Assert.Equal("bwrap", psi.FileName);
            Assert.Contains("--unshare-all", psi.ArgumentList);
            Assert.Contains("--die-with-parent", psi.ArgumentList);
            Assert.Contains("--ro-bind", psi.ArgumentList);
            // `--` separates bwrap args from the plugin executable.
            var sep = psi.ArgumentList.IndexOf("--");
            Assert.True(sep > 0);
            Assert.Equal("/usr/local/bin/demo-plugin", psi.ArgumentList[sep + 1]);
            Assert.Equal("--once", psi.ArgumentList[sep + 2]);
        }
        else
        {
            Assert.Equal("unshare", tag);
            Assert.Equal("unshare", psi.FileName);
            Assert.Contains("--net", psi.ArgumentList);
            Assert.Contains("--pid", psi.ArgumentList);
            Assert.Contains("--user", psi.ArgumentList);
            Assert.Contains("--map-root-user", psi.ArgumentList);
        }
    }

    [Fact]
    public void MacOsBuildLaunch_UsesSandboxExecOrFallsBackToNoop()
    {
        var psi = MacOsSandboxExecSandbox.BuildLaunch(Manifest(), new[] { "--once" });

        Assert.Contains("RADIOPAD_PLUGIN_ID", psi.Environment.Keys);
        var tag = psi.Environment["RADIOPAD_PLUGIN_SANDBOX"];
        if (tag == "sandbox-exec")
        {
            Assert.Equal("/usr/bin/sandbox-exec", psi.FileName);
            Assert.Equal("-p", psi.ArgumentList[0]);
            var profile = psi.ArgumentList[1];
            Assert.Contains("(deny default)", profile);
            Assert.Contains("(deny network*)", profile);
            Assert.Contains("(allow file-read*)", profile);
            // The work directory subpath is encoded in the profile.
            Assert.Contains("(allow file-write* (subpath \"", profile);
            Assert.Equal("/usr/local/bin/demo-plugin", psi.ArgumentList[2]);
            Assert.Equal("--once", psi.ArgumentList[3]);
        }
        else
        {
            // Fallback: sandbox-exec was missing on this host.
            Assert.Equal("noop", tag);
            Assert.Equal("/usr/local/bin/demo-plugin", psi.FileName);
        }
    }

    [Fact]
    public void MacOsSandboxProfile_IncludesWorkdirAndDeniesNetwork()
    {
        var profile = MacOsSandboxExecSandbox.BuildSandboxProfile("/tmp/radiopad-plugin-demo");
        Assert.Contains("(deny default)", profile);
        Assert.Contains("(deny network*)", profile);
        Assert.Contains("(allow file-read*)", profile);
        Assert.Contains("(allow file-write* (subpath \"/tmp/radiopad-plugin-demo\"))", profile);
    }

    [Fact]
    public void Factory_PicksOsAppropriateSandbox()
    {
        var sandbox = PluginSandboxFactory.CreateForCurrentOs();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.IsType<WindowsAppContainerSandbox>(sandbox);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Assert.IsType<LinuxNamespaceSandbox>(sandbox);
        else
            Assert.IsType<MacOsSandboxExecSandbox>(sandbox);
    }
}
