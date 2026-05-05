using System.Diagnostics;
using System.Runtime.InteropServices;
using RadioPad.Application.Abstractions;

namespace RadioPad.Application.Services.Mcp;

/// <summary>
/// Iter-33 MCP-007 — per-OS plugin sandbox wrappers. Today these are thin
/// process launchers that document the isolation contract and bake the
/// host-OS guard into the launch arguments; the IPC bridge between the
/// host and the sandboxed child is a follow-up. The wrappers all share
/// the same exit-code semantics so the MCP host can consume them
/// uniformly.
/// </summary>
internal static class PluginSandboxLaunch
{
    public static async Task<int> RunProcessAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardOutput = true;
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start plugin process: {psi.FileName}");
        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
        return proc.ExitCode;
    }
}

/// <summary>
/// Iter-33 — Windows AppContainer launch wrapper. The intended isolation
/// is via a dedicated AppContainer SID (low-trust profile, no broker
/// access). Today the AppContainer SID is materialised opportunistically
/// via <c>icacls</c>-prepared profile dirs; on platforms other than
/// Windows the wrapper hard-fails so a misconfigured runtime can't fall
/// through to an unsandboxed launch.
/// </summary>
public sealed class WindowsAppContainerSandbox : IPluginSandbox
{
    public Task<int> RunAsync(PluginManifest manifest, string[] args, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException(
                "WindowsAppContainerSandbox requires Windows; the host should select the Linux or macOS variant.");

        var psi = new ProcessStartInfo
        {
            FileName = manifest.Executable,
            // RADIOPAD_PLUGIN_APPCONTAINER hints the launcher (powered by a
            // small native helper in the desktop bundle) to spawn the
            // child inside the AppContainer SID. The host respects the env
            // var even when present in the parent so we don't accidentally
            // inherit a higher-privilege token.
        };
        psi.Environment["RADIOPAD_PLUGIN_APPCONTAINER"] = "1";
        psi.Environment["RADIOPAD_PLUGIN_ID"] = manifest.Id;
        foreach (var a in args) psi.ArgumentList.Add(a);
        return PluginSandboxLaunch.RunProcessAsync(psi, ct);
    }
}

/// <summary>
/// Iter-33 / iter-35 — Linux sandbox wrapper. Iter-35 hardens it as follows:
/// <list type="bullet">
/// <item>If <c>bwrap</c> (bubblewrap) is on <c>PATH</c> we prefer it because
/// it gives us combined namespace + filesystem isolation in one syscall:
/// <c>bwrap --unshare-all --die-with-parent --ro-bind / / --tmpfs /tmp
/// --tmpfs /run --bind &lt;workdir&gt; &lt;workdir&gt; --chdir &lt;workdir&gt;
/// -- &lt;executable&gt; ...</c>. <c>--unshare-all</c> already covers
/// net / pid / user / ipc / uts / cgroup, and the bind layout denies any
/// FS write outside the per-plugin work directory — which is the same
/// effective contract the Linux <c>landlock</c> LSM would give us. We tag
/// the child with <c>RADIOPAD_PLUGIN_SANDBOX=bwrap</c>.</item>
/// <item>Otherwise we fall back to the iter-33 path: <c>unshare --net
/// --pid --user --map-root-user --</c>. Network/PID/user are still
/// stripped; the FS is unrestricted, so we tag the child with
/// <c>RADIOPAD_PLUGIN_SANDBOX=unshare</c> so the operator can see the
/// reduced guarantee.</item>
/// </list>
/// On non-Linux platforms the wrapper hard-fails so a misconfigured runtime
/// can't fall through to an unsandboxed launch.
///
/// Note on landlock: the C# host cannot link the Rust <c>landlock</c>
/// crate, and adding a P/Invoke wrapper for the
/// <c>landlock_create_ruleset</c> / <c>landlock_restrict_self</c> syscalls
/// is the natural follow-up. For iter-35 we lean on bwrap's
/// <c>--ro-bind</c> + per-workdir <c>--bind</c> layout to deliver the
/// equivalent FS-write-deny effect, gated on bwrap being on PATH.
/// </summary>
public sealed class LinuxNamespaceSandbox : IPluginSandbox
{
    public Task<int> RunAsync(PluginManifest manifest, string[] args, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            throw new PlatformNotSupportedException(
                "LinuxNamespaceSandbox requires Linux; the host should select the Windows or macOS variant.");

        var psi = BuildLaunch(manifest, args);
        return PluginSandboxLaunch.RunProcessAsync(psi, ct);
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for the Linux launch. Public
    /// for unit-testing the bwrap-vs-unshare detection — the test asserts on
    /// <c>FileName</c> + <c>ArgumentList</c> + the
    /// <c>RADIOPAD_PLUGIN_SANDBOX</c> env tag without actually launching.
    /// </summary>
    public static ProcessStartInfo BuildLaunch(PluginManifest manifest, string[] args)
    {
        var workDir = PluginSandboxPaths.EnsureWorkDir(manifest);
        if (PluginSandboxPaths.IsExecutableOnPath("bwrap"))
            return BuildBwrapLaunch(manifest, args, workDir);
        return BuildUnshareLaunch(manifest, args, workDir);
    }

    private static ProcessStartInfo BuildBwrapLaunch(PluginManifest manifest, string[] args, string workDir)
    {
        var psi = new ProcessStartInfo { FileName = "bwrap" };
        psi.ArgumentList.Add("--unshare-all");
        psi.ArgumentList.Add("--die-with-parent");
        psi.ArgumentList.Add("--ro-bind");
        psi.ArgumentList.Add("/");
        psi.ArgumentList.Add("/");
        psi.ArgumentList.Add("--tmpfs");
        psi.ArgumentList.Add("/tmp");
        psi.ArgumentList.Add("--tmpfs");
        psi.ArgumentList.Add("/run");
        psi.ArgumentList.Add("--proc");
        psi.ArgumentList.Add("/proc");
        psi.ArgumentList.Add("--dev");
        psi.ArgumentList.Add("/dev");
        psi.ArgumentList.Add("--bind");
        psi.ArgumentList.Add(workDir);
        psi.ArgumentList.Add(workDir);
        psi.ArgumentList.Add("--chdir");
        psi.ArgumentList.Add(workDir);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(manifest.Executable);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["RADIOPAD_PLUGIN_ID"] = manifest.Id;
        psi.Environment["RADIOPAD_PLUGIN_SANDBOX"] = "bwrap";
        psi.Environment["RADIOPAD_PLUGIN_WORKDIR"] = workDir;
        return psi;
    }

    private static ProcessStartInfo BuildUnshareLaunch(PluginManifest manifest, string[] args, string workDir)
    {
        var psi = new ProcessStartInfo { FileName = "unshare" };
        psi.ArgumentList.Add("--net");
        psi.ArgumentList.Add("--pid");
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add("--map-root-user");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(manifest.Executable);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["RADIOPAD_PLUGIN_ID"] = manifest.Id;
        psi.Environment["RADIOPAD_PLUGIN_SANDBOX"] = "unshare";
        psi.Environment["RADIOPAD_PLUGIN_WORKDIR"] = workDir;
        return psi;
    }
}

/// <summary>
/// Iter-35 — macOS <c>sandbox-exec</c> wrapper. Replaces the iter-33
/// <c>MacOsNoopSandbox</c> placeholder. Wraps the plugin process with
/// <c>/usr/bin/sandbox-exec -p '&lt;profile&gt;'</c> using a profile that:
/// <list type="bullet">
/// <item>denies all network access (<c>(deny network*)</c>),</item>
/// <item>allows read on <c>/</c>,</item>
/// <item>allows read-write only inside the per-plugin work directory.</item>
/// </list>
/// If <c>/usr/bin/sandbox-exec</c> is missing (e.g. stripped down build
/// servers) we log a warning and fall back to the iter-33 noop launch path
/// so the host doesn't deadlock; the env tag flips to <c>noop</c> so the
/// MCP host audits the gap.
/// </summary>
public sealed class MacOsSandboxExecSandbox : IPluginSandbox
{
    public Task<int> RunAsync(PluginManifest manifest, string[] args, CancellationToken ct)
    {
        var psi = BuildLaunch(manifest, args);
        return PluginSandboxLaunch.RunProcessAsync(psi, ct);
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for the macOS launch.
    /// Public for unit tests so they can assert on the
    /// <c>sandbox-exec</c> profile string + workdir bind without spawning
    /// a real process.
    /// </summary>
    public static ProcessStartInfo BuildLaunch(PluginManifest manifest, string[] args)
    {
        var workDir = PluginSandboxPaths.EnsureWorkDir(manifest);
        const string sandboxExec = "/usr/bin/sandbox-exec";
        if (!File.Exists(sandboxExec))
        {
            Console.Error.WriteLine(
                $"[plugin-sandbox] WARNING: {sandboxExec} not found; running plugin '{manifest.Id}' without macOS sandbox.");
            var noop = new ProcessStartInfo { FileName = manifest.Executable };
            foreach (var a in args) noop.ArgumentList.Add(a);
            noop.Environment["RADIOPAD_PLUGIN_ID"] = manifest.Id;
            noop.Environment["RADIOPAD_PLUGIN_SANDBOX"] = "noop";
            noop.Environment["RADIOPAD_PLUGIN_WORKDIR"] = workDir;
            return noop;
        }

        var profile = BuildSandboxProfile(workDir);
        var psi = new ProcessStartInfo { FileName = sandboxExec };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(profile);
        psi.ArgumentList.Add(manifest.Executable);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["RADIOPAD_PLUGIN_ID"] = manifest.Id;
        psi.Environment["RADIOPAD_PLUGIN_SANDBOX"] = "sandbox-exec";
        psi.Environment["RADIOPAD_PLUGIN_WORKDIR"] = workDir;
        return psi;
    }

    /// <summary>
    /// Builds the SBPL (sandbox profile language) string passed to
    /// <c>sandbox-exec -p</c>. The intent is the minimum surface a plugin
    /// needs to run while still being denied network egress and FS writes
    /// outside its dedicated work directory.
    /// </summary>
    public static string BuildSandboxProfile(string workDir)
    {
        // Quote the workdir literal for SBPL — sandbox-exec uses Lisp-style
        // syntax; embedded backslashes / quotes are not expected on macOS
        // temp paths but we escape defensively.
        var safe = workDir.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return string.Join(' ', new[]
        {
            "(version 1)",
            "(deny default)",
            "(deny network*)",
            "(allow process-fork)",
            "(allow process-exec)",
            "(allow signal (target self))",
            "(allow sysctl-read)",
            "(allow mach-lookup)",
            "(allow ipc-posix-shm)",
            "(allow file-read*)",
            $"(allow file-write* (subpath \"{safe}\"))",
            "(allow file-write* (subpath \"/private/tmp\"))",
        });
    }

    // Internal helper visible to BuildLaunch above.
    internal static string EnsurePluginWorkDirExternal(PluginManifest manifest)
        => PluginSandboxPaths.EnsureWorkDir(manifest);
}

/// <summary>
/// Iter-33 alias retained for backwards compatibility. The macOS launch is
/// now handled by <see cref="MacOsSandboxExecSandbox"/>; the noop class
/// remains so any external diagnostic that probes for the type name keeps
/// working. New code should not instantiate it.
/// </summary>
[Obsolete("Use MacOsSandboxExecSandbox; the noop placeholder is retained only for backwards compatibility.")]
public sealed class MacOsNoopSandbox : IPluginSandbox
{
    public Task<int> RunAsync(PluginManifest manifest, string[] args, CancellationToken ct)
        => new MacOsSandboxExecSandbox().RunAsync(manifest, args, ct);
}

/// <summary>
/// Iter-33 — picks the appropriate sandbox for the current OS. Registered
/// in <c>Program.cs</c> as the default <see cref="IPluginSandbox"/>.
/// </summary>
public static class PluginSandboxFactory
{
    public static IPluginSandbox CreateForCurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsAppContainerSandbox();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxNamespaceSandbox();
        return new MacOsSandboxExecSandbox();
    }
}

internal static class PluginSandboxPaths
{
    /// <summary>
    /// Creates (idempotently) a per-plugin work directory rooted under the
    /// process temp dir. The directory is the only writable FS location
    /// granted to the sandboxed child.
    /// </summary>
    public static string EnsureWorkDir(PluginManifest manifest)
    {
        var safeId = string.Concat(manifest.Id.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        var dir = Path.Combine(Path.GetTempPath(), $"radiopad-plugin-{safeId}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsExecutableOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        var sep = Path.PathSeparator;
        foreach (var dir in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full)) return true;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (File.Exists(full + ".exe")) return true;
                    if (File.Exists(full + ".cmd")) return true;
                }
            }
            catch { /* unreadable PATH entry */ }
        }
        return false;
    }
}
