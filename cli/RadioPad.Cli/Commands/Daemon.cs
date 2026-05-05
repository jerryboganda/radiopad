using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-002 — manage the local <c>radiopad-api</c> backend service. Supports
/// <c>start</c> / <c>stop</c> / <c>status</c> / <c>restart</c>. The PID +
/// start time are tracked in <c>~/.radiopad/daemon.pid</c>.
/// </summary>
public static class DaemonControl
{
    private const string ApiAssemblyName = "RadioPad.Api";

    public static int Status()
    {
        var rec = ReadPidRecord();
        if (rec is null || !TryGetProcess(rec.Pid, out var proc) || proc!.HasExited)
        {
            Console.WriteLine("not running");
            return 0;
        }
        Console.WriteLine($"running (pid={rec.Pid}, since={rec.StartedAt:O})");
        return 0;
    }

    public static int Start(string bind, int port)
    {
        var existing = ReadPidRecord();
        if (existing is not null && TryGetProcess(existing.Pid, out var proc) && !proc!.HasExited)
        {
            Console.WriteLine($"already running (pid={existing.Pid})");
            return 0;
        }

        var apiPath = LocateApiBinary();
        if (apiPath is null)
        {
            Console.Error.WriteLine("daemon: could not locate the radiopad-api binary alongside the CLI.");
            return CliRuntime.ExitFailure;
        }

        var psi = new ProcessStartInfo
        {
            FileName = apiPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : apiPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardInput = false,
        };
        if (apiPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(apiPath);
        var bindUrl = NormalizeBindUrl(bind, port);
        psi.Environment["RADIOPAD_BIND"] = bindUrl;
        psi.Environment["RADIOPAD_PORT"] = port.ToString();
        psi.Environment["ASPNETCORE_URLS"] = bindUrl;

        var p = Process.Start(psi);
        if (p is null)
        {
            Console.Error.WriteLine("daemon: failed to spawn the API process.");
            return CliRuntime.ExitFailure;
        }
        WritePidRecord(new PidRecord(p.Id, DateTimeOffset.UtcNow, bind, port));
        Console.WriteLine($"started (pid={p.Id}, bind={bindUrl})");
        return 0;
    }

    public static int Stop()
    {
        var rec = ReadPidRecord();
        if (rec is null || !TryGetProcess(rec.Pid, out var proc) || proc!.HasExited)
        {
            DeletePidRecord();
            Console.WriteLine("not running");
            return 0;
        }
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                proc.CloseMainWindow();
            }
            else
            {
                // SIGTERM = 15. Process.Kill(false) sends SIGTERM on POSIX in .NET 8.
                proc.Kill(entireProcessTree: false);
            }
            if (!proc.WaitForExit(5000))
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"daemon: stop failed — {ex.Message}");
            return CliRuntime.ExitFailure;
        }
        DeletePidRecord();
        Console.WriteLine($"stopped (pid={rec.Pid})");
        return 0;
    }

    public static int Restart(string bind, int port)
    {
        Stop();
        return Start(bind, port);
    }

    // --- helpers --------------------------------------------------------

    public sealed record PidRecord(int Pid, DateTimeOffset StartedAt, string Bind, int Port);

    public static PidRecord? ReadPidRecord()
    {
        if (!File.Exists(CliRuntime.PidFilePath)) return null;
        try
        {
            using var s = File.OpenRead(CliRuntime.PidFilePath);
            var doc = JsonDocument.Parse(s);
            var r = doc.RootElement;
            return new PidRecord(
                r.GetProperty("pid").GetInt32(),
                r.GetProperty("startedAt").GetDateTimeOffset(),
                r.TryGetProperty("bind", out var b) ? b.GetString() ?? "127.0.0.1" : "127.0.0.1",
                r.TryGetProperty("port", out var p) ? p.GetInt32() : 7457);
        }
        catch
        {
            return null;
        }
    }

    public static void WritePidRecord(PidRecord rec)
    {
        Directory.CreateDirectory(CliRuntime.ConfigDir);
        File.WriteAllText(CliRuntime.PidFilePath, JsonSerializer.Serialize(new
        {
            pid = rec.Pid,
            startedAt = rec.StartedAt,
            bind = rec.Bind,
            port = rec.Port,
        }));
    }

    public static void DeletePidRecord()
    {
        try { if (File.Exists(CliRuntime.PidFilePath)) File.Delete(CliRuntime.PidFilePath); }
        catch { /* best-effort */ }
    }

    private static bool TryGetProcess(int pid, out Process? proc)
    {
        try { proc = Process.GetProcessById(pid); return true; }
        catch { proc = null; return false; }
    }

    private static string NormalizeBindUrl(string bind, int port)
    {
        if (bind.Contains("://", StringComparison.Ordinal)) return bind;
        return $"http://{bind}:{port}";
    }

    /// <summary>
    /// Looks for the published API binary alongside the CLI executable
    /// (<c>radiopad-api[.exe]</c> or <c>RadioPad.Api.dll</c>). Falls back
    /// to <c>RADIOPAD_API_PATH</c> if set.
    /// </summary>
    private static string? LocateApiBinary()
    {
        var envPath = Environment.GetEnvironmentVariable("RADIOPAD_API_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

        var here = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(here, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "radiopad-api.exe" : "radiopad-api"),
            Path.Combine(here, $"{ApiAssemblyName}.exe"),
            Path.Combine(here, $"{ApiAssemblyName}.dll"),
            Path.Combine(here, "..", "api", $"{ApiAssemblyName}.dll"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
