using System.Net.Http.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-007 — register an AI provider via <c>POST /api/providers</c>. Each
/// supported adapter type has a known <c>baseUrl</c> shape and required
/// fields; the CLI normalises them and forwards a <c>SaveProviderDto</c>
/// payload. Compliance class is forced to <c>Sandbox</c> by default —
/// the operator must promote a provider to <c>PhiApproved</c>/<c>LocalOnly</c>
/// from the admin UI after the BAA / DPA is on file.
/// </summary>
public static class ProviderRegister
{
    public static readonly string[] SupportedTypes =
    {
        "azure-openai", "aws-bedrock", "gcp-vertex", "openai",
        "openai-compatible", "anthropic", "mock", "ollama",
    };

    /// <summary>
    /// Pure helper — builds the <c>SaveProviderDto</c> payload. Visible
    /// for unit tests so we never regress the wire shape per adapter.
    /// </summary>
    public static Dictionary<string, object?> BuildPayload(
        string type, string name, string? baseUrl, string model, string apiKeyRef,
        string compliance = "Sandbox", bool enabled = true, int priority = 100)
    {
        if (!SupportedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"unsupported provider type: {type}", nameof(type));

        var url = baseUrl ?? "";
        // Per-adapter sane defaults for endpoint URL when the operator omits --base-url.
        if (string.IsNullOrWhiteSpace(url))
        {
            url = type.ToLowerInvariant() switch
            {
                "openai" => "https://api.openai.com/v1",
                "anthropic" => "https://api.anthropic.com",
                "ollama" => "http://127.0.0.1:11434",
                "mock" => "mock://local",
                _ => "",
            };
        }

        return new Dictionary<string, object?>
        {
            ["id"] = (object?)null,
            ["name"] = name,
            ["adapter"] = type.ToLowerInvariant(),
            ["model"] = model,
            ["endpointUrl"] = url,
            ["apiKeySecretRef"] = apiKeyRef,
            ["compliance"] = compliance,
            ["enabled"] = enabled,
            ["priority"] = priority,
        };
    }

    public static async Task<int> RegisterAsync(
        string type, string name, string? baseUrl, string model, string apiKeyRef,
        CancellationToken ct)
    {
        Dictionary<string, object?> payload;
        try { payload = BuildPayload(type, name, baseUrl, model, apiKeyRef); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliRuntime.ExitInvalidInput;
        }

        if (!apiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase) && type != "mock" && type != "ollama")
        {
            Console.Error.WriteLine("--api-key-ref must be of the form 'env:<NAME>' (the literal env-var name).");
            return CliRuntime.ExitInvalidInput;
        }

        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var resp = await http.PostAsJsonAsync("/api/providers", payload, ct);
        Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
        // Body never echoes the api-key value — server returns only { id }.
        Console.WriteLine(await resp.Content.ReadAsStringAsync(ct));
        return resp.IsSuccessStatusCode ? 0 : CliRuntime.ExitFailure;
    }
}
