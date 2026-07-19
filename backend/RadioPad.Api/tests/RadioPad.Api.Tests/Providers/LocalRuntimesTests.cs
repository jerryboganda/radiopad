using System.IO.Compression;
using System.Runtime.InteropServices;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// The pinned llama-server runtime that powers the optional offline MedGemma formatter.
///
/// <para>Facts here were verified against the live GitHub release and a real download: the Windows
/// asset is 18,007,324 bytes with SHA-256 <c>01d5f308…</c>, and the archive is flat with 51 entries
/// in which <c>llama-server.exe</c> is a ~9 KB launcher stub whose implementation lives in
/// <c>llama-server-impl.dll</c>, alongside ~14 <c>ggml-cpu-*.dll</c> backends dlopened at runtime.
/// That last detail is why the provisioner extracts the WHOLE archive — a cherry-picked executable
/// cannot start.</para>
/// </summary>
public class LocalRuntimesTests
{
    [Fact]
    public void Pinned_Archive_Is_A_Cpu_Only_Build_At_The_Pinned_Tag()
    {
        var spec = LocalRuntimes.LlamaServerArchive();
        if (spec is null) return; // unsupported platform/arch — the card stays unavailable

        Assert.Contains(LocalRuntimes.LlamaServerTag, spec.Url, StringComparison.Ordinal);

        // The real invariant, and the one that holds on every platform: never an accelerated build.
        // A GPU variant would blow the CPU-only budget (§1) and be wrong on most clinical
        // workstations, so guard against someone "upgrading" the pin to one.
        foreach (var accel in new[] { "cuda", "hip", "sycl", "vulkan", "openvino", "adreno" })
            Assert.DoesNotContain(accel, spec.FileName, StringComparison.OrdinalIgnoreCase);

        // Only Windows spells the CPU build out ("win-cpu-x64"); upstream names the Linux CPU
        // build plainly "ubuntu-x64", so asserting "cpu" everywhere fails on a Linux runner.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Contains("cpu", spec.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pin_Carries_A_Real_Sha256_And_Size()
    {
        var spec = LocalRuntimes.LlamaServerArchive();
        if (spec is null) return;

        // A runtime binary is executed on a clinical workstation; unlike a tiny tokens.txt it must
        // never be installed without content verification.
        Assert.Equal(64, spec.Sha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", spec.Sha256);
        Assert.True(spec.SizeBytes > 1_000_000, "a real llama.cpp archive is megabytes, not bytes");
    }

    [Fact]
    public void Download_Url_Is_The_Stable_Github_Release_Form()
    {
        var spec = LocalRuntimes.LlamaServerArchive();
        if (spec is null) return;

        // Must be the stable github.com/.../releases/download/... form. The CDN redirect target is
        // a short-lived signed URL and would 403 once its token expires.
        Assert.StartsWith("https://github.com/ggml-org/llama.cpp/releases/download/", spec.Url, StringComparison.Ordinal);
        Assert.EndsWith(spec.FileName, spec.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Executable_Name_Matches_The_Platform()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama-server.exe" : "llama-server";
        Assert.Equal(expected, LocalRuntimes.LlamaServerExecutableName);
    }

    [Fact]
    public void Executable_Is_Found_Even_When_Nested()
    {
        // The Linux tarball nests everything under a top-level directory while the Windows zip is
        // flat, so resolution must not assume a layout.
        var root = Path.Combine(Path.GetTempPath(), "rp-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Path.Combine(root, "llama-b10068");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, LocalRuntimes.LlamaServerExecutableName), "stub");

            Assert.True(LocalRuntimes.IsLlamaServerInstalled(root));
            Assert.NotNull(LocalRuntimes.ResolveLlamaServerExecutable(root));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Missing_Runtime_Reports_Not_Installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "rp-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "README.txt"), "no server here");
            Assert.False(LocalRuntimes.IsLlamaServerInstalled(root));
            Assert.Null(LocalRuntimes.ResolveLlamaServerExecutable(root));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Runtime_Dir_Is_Separate_From_The_Model_Store()
    {
        var runtime = LocalRuntimes.ResolveRuntimeDir(LocalRuntimes.LlamaServerId);
        if (runtime is null) return;

        // Runtimes are executables, models are data. Mixing them would make "delete this model"
        // ambiguous and could remove the binary out from under a running server.
        Assert.Contains("runtimes", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine("com.radiopad.desktop", "models"), runtime, StringComparison.Ordinal);
    }
}

/// <summary>
/// Zip extraction must refuse entries that escape the target directory.
///
/// <para>The SHA-256 pin already establishes the bytes are the ones we reviewed, so this is defence
/// in depth — but an extractor that honours <c>../</c> paths is a classic arbitrary-file-write
/// primitive, and this one runs on a clinical workstation.</para>
/// </summary>
public class ZipSlipTests
{
    /// <summary>Invokes the provisioner's private ExtractZip via reflection — the guard is an
    /// implementation detail, but it is exactly the kind of detail that must not silently regress.</summary>
    private static void ExtractZip(string archive, string target)
    {
        var mi = typeof(SttModelProvisioner).GetMethod(
            "ExtractZip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mi);
        try { mi!.Invoke(null, new object[] { archive, target }); }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    [Fact]
    public void Traversal_Entry_Is_Rejected()
    {
        var work = Path.Combine(Path.GetTempPath(), "rp-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var archive = Path.Combine(work, "evil.zip");
        var target = Path.Combine(work, "out");
        Directory.CreateDirectory(target);

        try
        {
            using (var zs = new FileStream(archive, FileMode.Create))
            using (var zip = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../escaped.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("pwned");
            }

            Assert.Throws<InvalidOperationException>(() => ExtractZip(archive, target));
            Assert.False(File.Exists(Path.Combine(work, "escaped.txt")),
                "the traversal entry must not have been written outside the target dir");
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public void Normal_Entries_Extract_Including_Nested_Paths()
    {
        var work = Path.Combine(Path.GetTempPath(), "rp-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var archive = Path.Combine(work, "ok.zip");
        var target = Path.Combine(work, "out");

        try
        {
            using (var zs = new FileStream(archive, FileMode.Create))
            using (var zip = new ZipArchive(zs, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("llama-server.exe").Open())) w.Write("stub");
                using (var w = new StreamWriter(zip.CreateEntry("sub/ggml-cpu-haswell.dll").Open())) w.Write("dll");
            }

            Directory.CreateDirectory(target);
            ExtractZip(archive, target);

            Assert.True(File.Exists(Path.Combine(target, "llama-server.exe")));
            Assert.True(File.Exists(Path.Combine(target, "sub", "ggml-cpu-haswell.dll")));
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }
}
