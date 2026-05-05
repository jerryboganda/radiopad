using System.Net.Http.Headers;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// Iter-31 G — shared CLI runtime helpers. Centralises config loading,
/// HTTP client construction, headless mode, and the canonical exit codes
/// so every command exits consistently.
/// </summary>
public static class CliRuntime
{
    /// <summary>Generic failure (non-zero exit, recoverable).</summary>
    public const int ExitFailure = 1;
    /// <summary>Invalid input / missing required argument.</summary>
    public const int ExitInvalidInput = 2;
    /// <summary>Headless mode could not satisfy the request without prompting.</summary>
    public const int ExitHeadlessUnauthenticated = 3;
    /// <summary>Local PHI policy guard refused the action (CLI-008).</summary>
    public const int ExitPhiPolicyBlocked = 4;

    /// <summary>Set by Program.Main from the --headless global option.</summary>
    public static bool Headless { get; set; }

    public sealed record CliConfig(string Tenant, string User, string Server, string? AccessToken);

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".radiopad");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static CliConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath)) return new("dev", "radiologist@radiopad.local", "http://127.0.0.1:7457", null);
        using var s = File.OpenRead(ConfigPath);
        var doc = JsonDocument.Parse(s);
        var r = doc.RootElement;
        return new(
            r.TryGetProperty("tenant", out var t) ? t.GetString() ?? "dev" : "dev",
            r.TryGetProperty("user", out var u) ? u.GetString() ?? "radiologist@radiopad.local" : "radiologist@radiopad.local",
            r.TryGetProperty("server", out var sv) ? sv.GetString() ?? "http://127.0.0.1:7457" : "http://127.0.0.1:7457",
            r.TryGetProperty("accessToken", out var at) ? at.GetString() : null);
    }

    public static void SaveConfig(CliConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(new
        {
            tenant = cfg.Tenant,
            user = cfg.User,
            server = cfg.Server,
            accessToken = cfg.AccessToken,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// Loads the config, exiting with <see cref="ExitHeadlessUnauthenticated"/>
    /// when <see cref="Headless"/> is set and no usable config exists.
    /// </summary>
    public static CliConfig RequireConfig()
    {
        if (Headless && !File.Exists(ConfigPath))
        {
            Console.Error.WriteLine("headless: missing ~/.radiopad/config.json — run `radiopad login` first.");
            Environment.Exit(ExitHeadlessUnauthenticated);
        }
        return LoadConfig();
    }

    public static HttpClient NewHttpClient(CliConfig? cfg = null)
    {
        cfg ??= LoadConfig();
        var http = new HttpClient { BaseAddress = new Uri(cfg.Server) };
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", cfg.Tenant);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", cfg.User);
        if (!string.IsNullOrEmpty(cfg.AccessToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.AccessToken);
        }
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public static string PidFilePath => Path.Combine(ConfigDir, "daemon.pid");
    public static string AuditSyncStatePath => Path.Combine(ConfigDir, "audit-sync.state");
    public static string DefaultAuditEventsPath => Path.Combine(ConfigDir, "audit-events.ndjson");
}
