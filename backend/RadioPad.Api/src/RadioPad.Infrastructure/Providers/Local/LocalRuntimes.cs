using System.Runtime.InteropServices;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Pinned on-demand RUNTIME binaries — currently just llama.cpp's <c>llama-server</c>, which
/// executes the optional local MedGemma formatter (dictation brief §2.2).
///
/// <para><b>Why on-demand rather than bundled.</b> Cloud formatting stays the default (decision
/// D1), so the offline formatter is a minority feature; shipping ~17 MB of runtime in every
/// installer to serve it would tax every user for something most never enable. Provisioning it the
/// same way models are provisioned keeps the installer small and reuses the download → verify →
/// extract → publish machinery that already exists.</para>
///
/// <para><b>Why the tag is pinned and not resolved to "latest".</b> llama.cpp cuts a release per
/// merged commit — tags advance several times a day. Resolving "latest" at runtime would mean every
/// workstation silently running a different, unreviewed build of a binary that processes PHI. The
/// tag here is a deliberate version we bump, exactly like a model pin.</para>
/// </summary>
public static class LocalRuntimes
{
    /// <summary>Catalog id == the runtimes/&lt;id&gt; folder the archive is extracted into.</summary>
    public const string LlamaServerId = "llama-server-b10068";

    /// <summary>The pinned llama.cpp release tag. Bump deliberately; see the class remarks.</summary>
    public const string LlamaServerTag = "b10068";

    /// <summary>llama.cpp is MIT licensed. NOTE: the Windows zip does NOT contain a LICENSE file
    /// (upstream CI copies it for Linux only), so the attribution is carried here.</summary>
    public const string LlamaServerLicense = "MIT (llama.cpp — Copyright (c) 2023-2026 The ggml authors)";

    /// <summary>
    /// The executable to launch, relative to the extracted runtime directory.
    ///
    /// <para><b>Do not cherry-pick this file.</b> On Windows <c>llama-server.exe</c> is a ~9 KB
    /// launcher stub; the implementation lives in <c>llama-server-impl.dll</c>, and
    /// <c>ggml-base.dll</c> dlopens one of ~14 <c>ggml-cpu-*.dll</c> micro-architecture backends at
    /// runtime. Extracting only the exe produces a binary that cannot start, so the whole archive
    /// is extracted.</para>
    /// </summary>
    public static string LlamaServerExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama-server.exe" : "llama-server";

    /// <summary>
    /// The archive for the current platform, or null where we have no pinned build. CPU-only
    /// variants deliberately: the desktop budget is CPU-only (§1) and a CUDA build would be both
    /// far larger and wrong on most clinical workstations.
    /// </summary>
    public static LocalSttModels.FileSpec? LlamaServerArchive()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return null; // no pinned arm64 build yet — the card stays unavailable rather than lying

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new LocalSttModels.FileSpec(
                Name: LlamaServerId,
                FileName: $"llama-{LlamaServerTag}-bin-win-cpu-x64.zip",
                Url: $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaServerTag}/llama-{LlamaServerTag}-bin-win-cpu-x64.zip",
                SizeBytes: 18007324L,
                Sha256: "01d5f30876acfb4a0be59396710f450213495c7181d8fbcce2fad045835ceb89");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LocalSttModels.FileSpec(
                Name: LlamaServerId,
                FileName: $"llama-{LlamaServerTag}-bin-ubuntu-x64.tar.gz",
                Url: $"https://github.com/ggml-org/llama.cpp/releases/download/{LlamaServerTag}/llama-{LlamaServerTag}-bin-ubuntu-x64.tar.gz",
                SizeBytes: 16066558L,
                Sha256: "6bf3d20de562e4df230f1a7c54fb7a06a80c7ff40f5311c953e8255744be4eb2");

        return null; // macOS is out of the desktop matrix until Apple signing is configured
    }

    /// <summary>Directory the runtime is installed into (sibling of the model store).</summary>
    public static string? ResolveRuntimeDir(string id)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData)) return null;
        return Path.Combine(localAppData, "com.radiopad.desktop", "runtimes", id);
    }

    /// <summary>
    /// Locate the runnable llama-server under <paramref name="dir"/>, or null when absent. Searched
    /// recursively because the Linux tarball nests everything under a top-level directory while the
    /// Windows zip is flat.
    /// </summary>
    public static string? ResolveLlamaServerExecutable(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        return Directory
            .GetFiles(dir, LlamaServerExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>True when a runnable llama-server is installed.</summary>
    public static bool IsLlamaServerInstalled(string? dir) => ResolveLlamaServerExecutable(dir) is not null;
}
