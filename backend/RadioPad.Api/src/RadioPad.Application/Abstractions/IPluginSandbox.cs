namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-33 MCP-007 — minimal cross-platform plugin sandbox abstraction.
/// Implementations spawn the plugin's executable inside an OS-specific
/// jail and return the child process exit code. The wrappers in this
/// iteration are stubs (the AppContainer SID / Linux namespaces are
/// configured but no IPC bridge is wired yet) — what matters for v0.1 is
/// that the launch path is funnelled through a single seam so a future
/// iteration can swap in a real isolation backend without changing the
/// host. macOS falls back to a noop <c>ProcessStartInfo</c> until a
/// <c>sandbox-exec</c> profile lands.
/// </summary>
public interface IPluginSandbox
{
    Task<int> RunAsync(PluginManifest manifest, string[] args, CancellationToken ct);
}

/// <summary>
/// Iter-33 — descriptor passed to <see cref="IPluginSandbox.RunAsync"/>.
/// Kept intentionally small (not a full domain entity) so the sandbox
/// surface stays decoupled from the trust-store schema.
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    string Executable,
    IReadOnlyList<string> Capabilities);
