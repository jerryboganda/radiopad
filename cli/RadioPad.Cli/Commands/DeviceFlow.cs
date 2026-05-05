using System.Net.Http.Json;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-001 — RFC 8628 OAuth 2.0 Device Authorization Grant client.
/// Drives <c>POST /api/auth/device/authorize</c> + <c>POST /api/auth/device/token</c>
/// against the backend (already shipped in iter-22). The access token is
/// persisted into <c>~/.radiopad/config.json</c> so subsequent commands send
/// <c>Authorization: Bearer …</c>. Tokens are never logged.
/// </summary>
public static class DeviceFlow
{
    public static async Task<int> RunAsync(string tenant, string user, string server, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(server) };
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", tenant);

        var authorize = await http.PostAsJsonAsync("/api/auth/device/authorize", new { clientId = "radiopad-cli" }, ct);
        if (!authorize.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"device-flow: authorize failed ({(int)authorize.StatusCode}).");
            return CliRuntime.ExitFailure;
        }
        using var ad = JsonDocument.Parse(await authorize.Content.ReadAsStringAsync(ct));
        var deviceCode = ad.RootElement.GetProperty("deviceCode").GetString()!;
        var userCode = ad.RootElement.GetProperty("userCode").GetString()!;
        var verificationUri = ad.RootElement.GetProperty("verificationUri").GetString()!;
        var interval = ad.RootElement.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;
        var expiresIn = ad.RootElement.TryGetProperty("expiresIn", out var ev) ? ev.GetInt32() : 600;

        Console.WriteLine($"open: {server.TrimEnd('/')}{verificationUri}?code={userCode}");
        Console.WriteLine($"code: {userCode}");
        Console.WriteLine($"polling every {interval}s (expires in {expiresIn}s)…");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        var pollInterval = interval;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollInterval), ct);
            var poll = await http.PostAsJsonAsync("/api/auth/device/token", new
            {
                deviceCode,
                grantType = "urn:ietf:params:oauth:grant-type:device_code",
            }, ct);

            if (poll.IsSuccessStatusCode)
            {
                using var tk = JsonDocument.Parse(await poll.Content.ReadAsStringAsync(ct));
                var accessToken = tk.RootElement.GetProperty("accessToken").GetString()!;
                var tenantSlug = tk.RootElement.GetProperty("tenant").GetString() ?? tenant;
                var userEmail = tk.RootElement.GetProperty("user").GetString() ?? user;
                CliRuntime.SaveConfig(new CliRuntime.CliConfig(tenantSlug, userEmail, server, accessToken));
                Console.WriteLine($"signed in: tenant={tenantSlug}, user={userEmail}");
                return 0;
            }

            // Treat 4xx body { error: ... } per RFC 8628.
            string error = "";
            try
            {
                using var ed = JsonDocument.Parse(await poll.Content.ReadAsStringAsync(ct));
                if (ed.RootElement.TryGetProperty("error", out var e)) error = e.GetString() ?? "";
            }
            catch
            {
                // ignore — fall through to error mapping by status code.
            }

            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    pollInterval += 5;
                    continue;
                case "access_denied":
                    Console.Error.WriteLine("device-flow: access_denied.");
                    return CliRuntime.ExitFailure;
                case "expired_token":
                    Console.Error.WriteLine("device-flow: expired_token.");
                    return CliRuntime.ExitFailure;
                default:
                    Console.Error.WriteLine($"device-flow: {(error.Length > 0 ? error : poll.StatusCode.ToString())}");
                    return CliRuntime.ExitFailure;
            }
        }
        Console.Error.WriteLine("device-flow: timed out waiting for approval.");
        return CliRuntime.ExitFailure;
    }
}
