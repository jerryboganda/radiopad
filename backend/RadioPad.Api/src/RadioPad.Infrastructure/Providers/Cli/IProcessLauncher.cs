using System.Diagnostics;
using System.Text;

namespace RadioPad.Infrastructure.Providers.Cli;

/// <summary>
/// Iter-36 — abstraction over <see cref="Process"/> so CLI provider
/// adapters can be unit-tested without spawning real binaries. The default
/// implementation, <see cref="DefaultProcessLauncher"/>, uses
/// <see cref="ProcessStartInfo.ArgumentList"/> (never the legacy
/// <c>Arguments</c> string) so callers cannot accidentally introduce shell
/// injection.
/// </summary>
public interface IProcessLauncher
{
    Task<ProcessLaunchResult> RunAsync(ProcessLaunchSpec spec, CancellationToken ct);
}

public sealed record ProcessLaunchSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    int TimeoutMs);

public sealed record ProcessLaunchResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    long ElapsedMs);

/// <summary>
/// Raised by <see cref="IProcessLauncher"/> implementations when the
/// configured binary cannot be located or fails to start. CLI provider
/// adapters translate this into <see cref="Application.Services.ProviderTransportException"/>.
/// </summary>
public sealed class ProcessLaunchNotFoundException : Exception
{
    public ProcessLaunchNotFoundException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Raised by <see cref="IProcessLauncher"/> implementations when the
/// process exceeds <see cref="ProcessLaunchSpec.TimeoutMs"/>. The launcher
/// must have killed the process tree before throwing.
/// </summary>
public sealed class ProcessLaunchTimeoutException : Exception
{
    public long ElapsedMs { get; }
    public ProcessLaunchTimeoutException(string message, long elapsedMs) : base(message)
    {
        ElapsedMs = elapsedMs;
    }
}

public sealed class DefaultProcessLauncher : IProcessLauncher
{
    public async Task<ProcessLaunchResult> RunAsync(ProcessLaunchSpec spec, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardInput = spec.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = EnsureWorkingDirectory(),
        };
        ScrubEnvironment(psi);
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        var sw = Stopwatch.StartNew();
        try
        {
            if (!proc.Start())
                throw new ProcessLaunchNotFoundException($"failed to start '{spec.FileName}'");
        }
        catch (System.ComponentModel.Win32Exception w32)
        {
            throw new ProcessLaunchNotFoundException($"binary '{spec.FileName}' not found on PATH: {w32.Message}", w32);
        }
        catch (FileNotFoundException fnf)
        {
            throw new ProcessLaunchNotFoundException($"binary '{spec.FileName}' not found: {fnf.Message}", fnf);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (spec.StandardInput is not null)
        {
            try
            {
                await proc.StandardInput.WriteAsync(spec.StandardInput);
                proc.StandardInput.Close();
            }
            catch (IOException) { /* process already exited / closed stdin */ }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(spec.TimeoutMs);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new ProcessLaunchTimeoutException(
                $"process '{spec.FileName}' exceeded timeout of {spec.TimeoutMs} ms",
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        sw.Stop();

        return new ProcessLaunchResult(
            ExitCode: proc.ExitCode,
            StandardOutput: stdout.ToString(),
            StandardError: stderr.ToString(),
            ElapsedMs: sw.ElapsedMilliseconds);
    }

    private static string EnsureWorkingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "radiopad-cli-provider");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ScrubEnvironment(ProcessStartInfo psi)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH",
            "PATHEXT",
            "SystemRoot",
            "WINDIR",
            "HOME",
            "USERPROFILE",
            "APPDATA",
            "LOCALAPPDATA",
            "TMP",
            "TEMP",
        };
        var extra = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_ENV_ALLOWLIST");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            foreach (var name in extra.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                names.Add(name);
            }
        }

        psi.Environment.Clear();
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) psi.Environment[name] = value;
        }
    }
}
