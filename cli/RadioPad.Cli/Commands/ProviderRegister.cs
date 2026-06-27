using System.Net.Http.Json;
using RadioPad.Domain.Enums;

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
        "azure-openai", "aws-bedrock", "google-vertex", "gcp-vertex", "vertex-ai", "google-vertex-ai",
        "openai", "openai-direct", "openai-compatible", "anthropic", "mock", "ollama", "ollama-chat",
        "vllm", "llama-cpp", "gemini-cli", "codex-cli",
    };

    /// <summary>
    /// Pure helper — builds the <c>SaveProviderDto</c> payload. Visible
    /// for unit tests so we never regress the wire shape per adapter.
    /// </summary>
    public static Dictionary<string, object?> BuildPayload(
        string type, string name, string? baseUrl, string model, string? apiKeyRef,
        ProviderComplianceClass compliance = ProviderComplianceClass.Sandbox, bool enabled = true, int priority = 100)
    {
        if (!SupportedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"unsupported provider type: {type}", nameof(type));

        var adapter = CanonicalAdapter(type);
        var url = baseUrl ?? "";
        // Per-adapter sane defaults for endpoint URL when the operator omits --base-url.
        if (string.IsNullOrWhiteSpace(url))
        {
            url = adapter switch
            {
                "openai" => "https://api.openai.com/v1",
                "anthropic" => "https://api.anthropic.com",
                "ollama" or "ollama-chat" => "http://127.0.0.1:11434",
                "mock" => "mock://local",
                _ => "",
            };
        }

        return new Dictionary<string, object?>
        {
            ["id"] = (object?)null,
            ["name"] = name,
            ["adapter"] = adapter,
            ["model"] = model,
            ["endpointUrl"] = url,
            ["apiKeySecretRef"] = apiKeyRef ?? string.Empty,
            ["compliance"] = (int)compliance,
            ["enabled"] = enabled,
            ["priority"] = priority,
        };
    }

    public static async Task<int> RegisterAsync(
        string type, string name, string? baseUrl, string model, string? apiKeyRef,
        CancellationToken ct)
    {
        Dictionary<string, object?> payload;
        try { payload = BuildPayload(type, name, baseUrl, model, apiKeyRef); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliRuntime.ExitInvalidInput;
        }

        var adapter = CanonicalAdapter(type);
        apiKeyRef ??= string.Empty;
        if (!string.IsNullOrEmpty(apiKeyRef) && !apiKeyRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("--api-key-ref must be of the form 'env:<NAME>' (the literal env-var name)." );
            return CliRuntime.ExitInvalidInput;
        }
        if (RequiresApiKey(adapter) && string.IsNullOrEmpty(apiKeyRef))
        {
            Console.Error.WriteLine($"--api-key-ref is required for provider type '{adapter}' and must be of the form 'env:<NAME>'.");
            return CliRuntime.ExitInvalidInput;
        }

        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var resp = await http.PostAsJsonAsync("/api/providers", payload, ct);
        Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
        // Body never echoes the api-key value — server returns only { id }.
        Console.WriteLine(await resp.Content.ReadAsStringAsync(ct));
        return resp.IsSuccessStatusCode ? 0 : CliRuntime.ExitFailure;
    }

    private static string CanonicalAdapter(string type) => type.ToLowerInvariant() switch
    {
        "gcp-vertex" or "vertex-ai" or "google-vertex-ai" => "google-vertex",
        "openai-direct" => "openai",
        _ => type.ToLowerInvariant(),
    };

    private static bool RequiresApiKey(string adapter) => adapter is
        "anthropic" or "openai" or "azure-openai" or "aws-bedrock" or "google-vertex";
}
