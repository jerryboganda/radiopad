using System.Net.Http.Json;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// Operator org bootstrap commands — thin HTTP wrappers over the secret-gated
/// <c>POST /api/admin/bootstrap-org</c> endpoint. The super-admin secret is only
/// ever read from <c>RADIOPAD_BOOTSTRAP_SECRET</c> so it never appears on the
/// command line or in shell history.
/// </summary>
public static class OrgBootstrap
{
    private const string SecretEnv = "RADIOPAD_BOOTSTRAP_SECRET";

    public static async Task<int> CreateAsync(
        string server, string slug, string? name, string adminEmail, string? adminName, string? tempPassword, CancellationToken ct)
    {
        var secret = Environment.GetEnvironmentVariable(SecretEnv);
        if (string.IsNullOrWhiteSpace(secret))
        {
            Console.Error.WriteLine($"{SecretEnv} environment variable is not set.");
            return CliRuntime.ExitInvalidInput;
        }

        using var http = NewClient(server, secret);
        var resp = await http.PostAsJsonAsync("/api/admin/bootstrap-org",
            new { slug, name, adminEmail, adminName, tempPassword }, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(raw);
            return CliRuntime.ExitFailure;
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        Console.WriteLine("Organization created.");
        Console.WriteLine($"  slug:          {root.GetProperty("slug").GetString()}");
        Console.WriteLine($"  admin email:   {root.GetProperty("adminEmail").GetString()}");
        Console.WriteLine($"  temp password: {root.GetProperty("tempPassword").GetString()}");
        Console.WriteLine();
        Console.WriteLine("Hand this to the admin. They sign in with it and are required to set up");
        Console.WriteLine("an authenticator app (TOTP) on first login. No email is sent.");
        return 0;
    }

    public static async Task<int> ResetAdminAsync(
        string server, string slug, string email, string? tempPassword, CancellationToken ct)
    {
        var secret = Environment.GetEnvironmentVariable(SecretEnv);
        if (string.IsNullOrWhiteSpace(secret))
        {
            Console.Error.WriteLine($"{SecretEnv} environment variable is not set.");
            return CliRuntime.ExitInvalidInput;
        }

        using var http = NewClient(server, secret);
        var resp = await http.PostAsJsonAsync("/api/admin/bootstrap-org/reset-admin",
            new { slug, email, tempPassword }, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(raw);
            return CliRuntime.ExitFailure;
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var created = root.TryGetProperty("created", out var c) && c.GetBoolean();
        Console.WriteLine(created ? "Master admin created and reset." : "Master admin reset.");
        Console.WriteLine($"  slug:          {root.GetProperty("slug").GetString()}");
        Console.WriteLine($"  admin email:   {root.GetProperty("email").GetString()}");
        Console.WriteLine($"  temp password: {root.GetProperty("tempPassword").GetString()}");
        return 0;
    }

    private static HttpClient NewClient(string server, string secret)
    {
        var http = new HttpClient { BaseAddress = new Uri(server) };
        http.DefaultRequestHeaders.Add("X-RadioPad-Bootstrap", secret);
        return http;
    }
}
