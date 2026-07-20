using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Starts and supervises the on-demand <c>llama-server</c> child process that runs the optional
/// local MedGemma formatter (dictation brief §2.2).
///
/// <para><b>Why the sidecar owns this rather than the Tauri shell.</b> The formatter is the only
/// consumer, it knows exactly when a request needs the model loaded, and keeping the process inside
/// the same lifetime means a crash of either takes both down together instead of leaving an
/// orphaned 2.5 GB-resident server behind. It also keeps the whole offline path working when the
/// sidecar is run without the desktop shell.</para>
///
/// <para><b>Bound to loopback only.</b> PHI reaches this process, so it must never be reachable off
/// the workstation; the bind address is not configurable to anything else.</para>
///
/// <para>Started lazily on first use — loading a 2.5 GB model costs seconds and a lot of RAM, so
/// paying that at sidecar boot for a feature that is off by default would be wrong.</para>
/// </summary>
public sealed class LlamaServerProcess : IDisposable
{
    /// <summary>Loopback only — see the class remarks. Port is the llama.cpp default.</summary>
    public const string Host = "127.0.0.1";
    public const int Port = 8080;
    public static string BaseUrl => $"http://{Host}:{Port}";

    private readonly ILogger<LlamaServerProcess> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _disposed;

    public LlamaServerProcess(ILogger<LlamaServerProcess> log) => _log = log;

    /// <summary>True when a llama-server we started is still running.</summary>
    public bool IsRunning
    {
        get
        {
            var p = _process;
            try { return p is not null && !p.HasExited; }
            catch (InvalidOperationException) { return false; } // never started / already reaped
        }
    }

    /// <summary>
    /// Ensure a llama-server is serving <paramref name="modelPath"/>, starting one if needed.
    /// Returns the base URL, or null when the runtime or model is not installed.
    ///
    /// <para>Does NOT start anything if the caller's prerequisites are missing — the formatter then
    /// reports an actionable message instead of the process silently failing to launch.</para>
    /// </summary>
    public async Task<string?> EnsureRunningAsync(string modelPath, CancellationToken ct)
    {
        if (IsRunning) return BaseUrl;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsRunning) return BaseUrl;

            var runtimeDir = LocalRuntimes.ResolveRuntimeDir(LocalRuntimes.LlamaServerId);
            var exe = LocalRuntimes.ResolveLlamaServerExecutable(runtimeDir);
            if (exe is null)
            {
                _log.LogWarning("llama-server runtime is not installed; cannot start the offline formatter.");
                return null;
            }
            if (!File.Exists(modelPath))
            {
                _log.LogWarning("MedGemma model {Path} is not present; cannot start the offline formatter.", modelPath);
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                // WorkingDirectory matters: on Windows the launcher stub resolves its sibling
                // llama-server-impl.dll and the ggml-cpu-*.dll backends relative to itself.
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(modelPath);
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add(Host);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(Port.ToString());
            // CPU-only budget (§1). Leave a core for the UI and the STT engine, and cap the
            // ceiling: llama.cpp's throughput does not keep scaling past roughly 8 threads on
            // typical hardware, and on a many-core / multi-socket workstation blindly using
            // "all but one" logical processor oversubscribes far past that point — threads spend
            // more time synchronizing (and contending for cross-NUMA memory bandwidth) than
            // computing, which pins CPU near 100% AND makes generation slower, not faster. That
            // combination is exactly what turns a legitimate cold load into a client-visible
            // timeout. Override with RADIOPAD_LLAMA_THREADS for a workstation benchmarked to do
            // better with a different count.
            var threads = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_LLAMA_THREADS"), out var configuredThreads) && configuredThreads > 0
                ? configuredThreads
                : Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
            psi.ArgumentList.Add("--threads");
            psi.ArgumentList.Add(threads.ToString());
            psi.ArgumentList.Add("--ctx-size");
            psi.ArgumentList.Add("4096");

            _log.LogInformation("Starting llama-server for the offline formatter: {Exe}", exe);
            var process = Process.Start(psi);
            if (process is null)
            {
                _log.LogError("Process.Start returned no handle for llama-server.");
                return null;
            }

            // Drain the pipes: a child whose redirected stdout/stderr is never read will block once
            // the OS buffer fills, which would hang model loading partway through.
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) _log.LogDebug("llama-server: {Line}", e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log.LogDebug("llama-server: {Line}", e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            return BaseUrl;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Block until the server answers /health or the timeout expires. Loading a multi-GB GGUF on
    /// CPU legitimately takes tens of seconds, so callers must wait rather than treating the first
    /// refused connection as failure.
    /// </summary>
    public async Task<bool> WaitUntilHealthyAsync(HttpClient http, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRunning)
            {
                _log.LogError("llama-server exited before becoming healthy.");
                return false;
            }
            try
            {
                using var resp = await http.GetAsync($"{BaseUrl}/health", ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var p = _process;
        _process = null;
        if (p is null) return;

        try
        {
            if (!p.HasExited)
            {
                // Kill the whole tree: llama-server's Windows launcher stub runs the real server, so
                // killing only the parent would strand a process holding gigabytes of model.
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already gone — nothing to clean up.
        }
        finally
        {
            p.Dispose();
            _gate.Dispose();
        }
    }
}
