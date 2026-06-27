using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Whisper.net.LibraryLoader;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Makes the whisper.cpp native runtime loadable from the bundled desktop
/// sidecar.
///
/// The sidecar ships as a self-contained, single-file executable (Tauri
/// <c>externalBin</c>). Whisper.net's native loader requires its DLLs to sit on
/// disk in a <c>runtimes/&lt;rid&gt;/</c> folder under one of its search paths
/// (the app base dir, the assembly dir, or <see cref="RuntimeOptions.LibraryPath"/>).
/// Single-file publish breaks both halves of that contract: the managed assembly
/// has no on-disk <c>Location</c>, and the win-x64 DLLs — emitted by
/// Whisper.net.Runtime as loose content next to the published exe — are orphaned
/// because only the exe is copied into the Tauri sidecar. The result is the
/// runtime <c>FileNotFoundException: "Native Library not found in default paths"</c>.
/// (sherpa-onnx survives the same packaging because it loads via plain
/// <c>[DllImport]</c>, which resolves against .NET's self-extract native dir;
/// Whisper.net's path-based probe does not.)
///
/// We sidestep the packaging entirely: the four win-x64 DLLs are embedded into
/// this assembly (see <c>RadioPad.Infrastructure.csproj</c>) and, on first use,
/// re-materialized into a per-user cache laid out as <c>runtimes/win-x64/*.dll</c>.
/// We then point Whisper.net at that cache. This works identically under
/// <c>dotnet run</c>, <c>tauri dev</c>, and the bundled MSI, and is immune to how
/// the sidecar is packaged.
/// </summary>
public static class WhisperNativeLibrary
{
    // Must match the <LogicalName> prefix used for the EmbeddedResource items in
    // RadioPad.Infrastructure.csproj.
    private const string ResourcePrefix = "RadioPad.WhisperNative.win-x64.";

    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>The directory we extracted the natives into (containing
    /// <c>runtimes/win-x64</c>), or null when nothing was staged (non-Windows-x64
    /// host, natives not embedded, or extraction failed).</summary>
    public static string? ResolvedRuntimeDir { get; private set; }

    /// <summary>
    /// Idempotently extract the embedded win-x64 whisper natives and point
    /// Whisper.net's loader at them. Safe to call repeatedly and from any thread.
    /// A no-op on non-Windows-x64 hosts and when the natives are not embedded
    /// (e.g. a plain library/test build), where Whisper.net's default probing
    /// already locates the loose <c>runtimes/win-x64</c> next to the binary.
    ///
    /// MUST run before the first <see cref="Whisper.net.WhisperFactory"/> is
    /// created — <see cref="RuntimeOptions"/> is only consulted during the
    /// loader's one-time lazy init.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            try
            {
                // These natives are win-x64 only; elsewhere Whisper.net's own
                // probing (or the self-disabled engine) is the correct path.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && RuntimeInformation.OSArchitecture == Architecture.X64)
                {
                    var baseDir = ExtractIfNeeded();
                    if (baseDir is not null)
                    {
                        ResolvedRuntimeDir = baseDir;
                        // The loader derives its search base from
                        // Path.GetDirectoryName(LibraryPath) and then appends
                        // "runtimes/win-x64". So LibraryPath must be a file path
                        // whose *directory* is the parent of `runtimes` — the
                        // sentinel below never has to exist on disk; only its
                        // directory component is used.
                        RuntimeOptions.LibraryPath = Path.Combine(baseDir, "whisper.net.anchor");
                    }
                }
            }
            catch
            {
                // Never let native-staging throw on the hot path. If it fails,
                // Whisper.net falls back to its default search paths and the
                // engine surfaces its own load error to the diagnostics/test UI.
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    /// <summary>
    /// Materialize the embedded DLLs into
    /// <c>%LOCALAPPDATA%\com.radiopad.desktop\whisper-runtime\&lt;stamp&gt;\runtimes\win-x64\</c>
    /// and return the cache root (the parent of <c>runtimes</c>), or null when no
    /// natives are embedded. The stamp folds in the payload's size signature so a
    /// Whisper.net.Runtime version bump re-extracts into a fresh directory rather
    /// than serving stale DLLs.
    /// </summary>
    private static string? ExtractIfNeeded()
    {
        var asm = typeof(WhisperNativeLibrary).Assembly;
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();
        if (names.Length == 0)
            return null; // not embedded (library/test build) — use default probing.

        var stamp = ComputeStamp(asm, names);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            localAppData = Path.GetTempPath();

        var root = Path.Combine(localAppData, "com.radiopad.desktop", "whisper-runtime", stamp);
        var nativeDir = Path.Combine(root, "runtimes", "win-x64");
        Directory.CreateDirectory(nativeDir);

        foreach (var name in names)
        {
            var fileName = name.Substring(ResourcePrefix.Length);
            var target = Path.Combine(nativeDir, fileName);

            using var src = asm.GetManifestResourceStream(name);
            if (src is null) continue;

            // Skip the rewrite when an intact copy is already present, so repeat
            // launches don't churn the disk or fight a DLL that's mapped by a
            // still-running sibling process.
            if (File.Exists(target) && new FileInfo(target).Length == src.Length)
                continue;

            var tmp = target + ".tmp";
            using (var dst = File.Create(tmp))
                src.CopyTo(dst);
            File.Move(tmp, target, overwrite: true);
        }

        return root;
    }

    /// <summary>
    /// A short, deterministic stamp over the embedded payload (sorted
    /// name+length pairs). Length is a sufficient change signal for native DLLs
    /// and avoids hashing every byte on each launch.
    /// </summary>
    private static string ComputeStamp(Assembly asm, string[] names)
    {
        using var sha = SHA256.Create();
        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name);
            var len = s?.Length ?? 0;
            var bytes = Encoding.UTF8.GetBytes($"{name}:{len};");
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!)[..16].ToLowerInvariant();
    }
}
