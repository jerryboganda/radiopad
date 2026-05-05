using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// Iter-35 — CLI front-end for the validation pack endpoints under
/// <c>/api/validation-packs</c>. Mirrors the existing on-disk fixture
/// format under <c>rulebooks/_tests/&lt;rulebook_id&gt;/*.json</c> so a
/// pack can round-trip between disk and the backend.
/// </summary>
public static class ValidationPacksCommands
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<int> ListAsync(string? rulebookId, CancellationToken ct)
    {
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var path = "/api/validation-packs";
        if (!string.IsNullOrWhiteSpace(rulebookId)) path += $"?rulebookId={Uri.EscapeDataString(rulebookId)}";
        var json = await http.GetStringAsync(path, ct);
        using var doc = JsonDocument.Parse(json);
        Console.WriteLine($"{"rulebookId",-24}  {"version",-12}  {"status",-10}  {"cases",5}  id");
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var rb = TryString(p, "rulebookId");
            var ver = TryString(p, "version");
            var st = TryString(p, "status");
            var cc = p.TryGetProperty("caseCount", out var ccEl) ? ccEl.GetInt32() : 0;
            var id = TryString(p, "id");
            Console.WriteLine($"{rb,-24}  {ver,-12}  {st,-10}  {cc,5}  {id}");
        }
        return 0;
    }

    public static async Task<int> ImportAsync(
        string rulebookId,
        string version,
        string? name,
        DirectoryInfo dir,
        CancellationToken ct)
    {
        if (!dir.Exists)
        {
            Console.Error.WriteLine($"directory not found: {dir.FullName}");
            return CliRuntime.ExitInvalidInput;
        }
        var cases = new List<JsonElement>();
        foreach (var file in dir.EnumerateFiles("*.json").OrderBy(f => f.Name))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName, ct));
            cases.Add(doc.RootElement.Clone());
        }
        if (cases.Count == 0)
        {
            Console.Error.WriteLine("no *.json fixtures found in directory");
            return CliRuntime.ExitInvalidInput;
        }
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var payload = new
        {
            rulebookId,
            version,
            name = string.IsNullOrWhiteSpace(name) ? $"{rulebookId} v{version}" : name,
            goldenCases = cases,
        };
        using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await http.PostAsync("/api/validation-packs", body, ct);
        Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
        Console.WriteLine(await resp.Content.ReadAsStringAsync(ct));
        return resp.IsSuccessStatusCode ? 0 : CliRuntime.ExitFailure;
    }

    public static async Task<int> ExportAsync(string packId, FileInfo outFile, CancellationToken ct)
    {
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var resp = await http.GetAsync($"/api/validation-packs/{packId}/export", ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
            return CliRuntime.ExitFailure;
        }
        var raw = await resp.Content.ReadAsStringAsync(ct);
        outFile.Directory?.Create();
        // Pretty-print on the way out.
        using var doc = JsonDocument.Parse(raw);
        await File.WriteAllTextAsync(outFile.FullName,
            JsonSerializer.Serialize(doc.RootElement, JsonOpts), ct);
        Console.WriteLine($"saved {outFile.FullName}");
        return 0;
    }

    public static async Task<int> RunAsync(string packId, CancellationToken ct)
    {
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var resp = await http.PostAsync($"/api/validation-packs/{packId}/run", content: null, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(raw);
            return CliRuntime.ExitFailure;
        }
        using var doc = JsonDocument.Parse(raw);
        var passed = doc.RootElement.GetProperty("passed").GetInt32();
        var failed = doc.RootElement.GetProperty("failed").GetInt32();
        var total = doc.RootElement.GetProperty("totalCases").GetInt32();
        Console.WriteLine($"{passed}/{total} passed ({failed} failed)");
        if (doc.RootElement.TryGetProperty("failures", out var fails))
        {
            foreach (var f in fails.EnumerateArray())
            {
                var caseId = TryString(f, "caseId");
                var missing = string.Join(",", f.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
                var unexpected = string.Join(",", f.GetProperty("unexpected").EnumerateArray().Select(e => e.GetString()));
                Console.WriteLine($"  FAIL {caseId}  missing=[{missing}]  unexpected=[{unexpected}]");
            }
        }
        return failed == 0 ? 0 : CliRuntime.ExitFailure;
    }

    private static string TryString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
}
