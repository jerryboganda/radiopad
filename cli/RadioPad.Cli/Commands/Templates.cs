using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-006 — manage report templates against <c>/api/templates</c>. The
/// list endpoint returns the full template entities; <c>export</c> matches
/// against the stable <c>templateId</c> (or GUID <c>id</c>) and writes
/// JSON or YAML depending on the file extension. <c>import</c> reads a
/// JSON or YAML payload and POSTs the upsert (the backend keys on
/// <c>templateId</c>).
/// </summary>
public static class TemplatesCommands
{
    public static async Task<int> ListAsync(CancellationToken ct)
    {
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var json = await http.GetStringAsync("/api/templates", ct);
        using var doc = JsonDocument.Parse(json);
        Console.WriteLine($"{"templateId",-28}  {"name",-30}  {"modality",-8}  {"bodyPart",-14}  guid");
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            var tid = TryString(t, "templateId");
            var name = TryString(t, "name");
            var modality = TryString(t, "modality");
            var bp = TryString(t, "bodyPart");
            var id = TryString(t, "id");
            Console.WriteLine($"{tid,-28}  {name,-30}  {modality,-8}  {bp,-14}  {id}");
        }
        return 0;
    }

    public static async Task<int> ExportAsync(string idOrTemplateId, FileInfo outFile, CancellationToken ct)
    {
        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        var json = await http.GetStringAsync("/api/templates", ct);
        using var doc = JsonDocument.Parse(json);
        JsonElement? match = null;
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            if (string.Equals(TryString(t, "templateId"), idOrTemplateId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(TryString(t, "id"), idOrTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                match = t;
                break;
            }
        }
        if (match is null)
        {
            Console.Error.WriteLine($"template not found: {idOrTemplateId}");
            return CliRuntime.ExitFailure;
        }
        outFile.Directory?.Create();
        var ext = (outFile.Extension ?? "").ToLowerInvariant();
        if (ext is ".yaml" or ".yml")
        {
            var dict = ToDictionary(match.Value);
            var yaml = new SerializerBuilder().Build().Serialize(dict);
            await File.WriteAllTextAsync(outFile.FullName, yaml, ct);
        }
        else
        {
            await File.WriteAllTextAsync(outFile.FullName,
                JsonSerializer.Serialize(JsonDocument.Parse(match.Value.GetRawText()).RootElement,
                    new JsonSerializerOptions { WriteIndented = true }), ct);
        }
        Console.WriteLine($"saved {outFile.FullName}");
        return 0;
    }

    public static async Task<int> ImportAsync(FileInfo file, CancellationToken ct)
    {
        if (!file.Exists)
        {
            Console.Error.WriteLine($"template file not found: {file.FullName}");
            return CliRuntime.ExitInvalidInput;
        }
        var raw = await File.ReadAllTextAsync(file.FullName, ct);
        var ext = (file.Extension ?? "").ToLowerInvariant();
        var payload = BuildSavePayload(raw, ext);
        if (payload is null) return CliRuntime.ExitInvalidInput;

        using var http = CliRuntime.NewHttpClient(CliRuntime.RequireConfig());
        using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await http.PostAsync("/api/templates", body, ct);
        Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
        Console.WriteLine(await resp.Content.ReadAsStringAsync(ct));
        return resp.IsSuccessStatusCode ? 0 : CliRuntime.ExitFailure;
    }

    /// <summary>Visible for testing — converts a JSON or YAML template file into a SaveTemplateDto-shaped payload.</summary>
    public static Dictionary<string, object?>? BuildSavePayload(string raw, string ext)
    {
        Dictionary<string, object?> map;
        try
        {
            if (ext is ".yaml" or ".yml")
            {
                var deserializer = new DeserializerBuilder().Build();
                var node = deserializer.Deserialize<Dictionary<object, object?>>(raw)
                    ?? new Dictionary<object, object?>();
                map = node.ToDictionary(kv => kv.Key.ToString() ?? "", kv => kv.Value);
            }
            else
            {
                using var doc = JsonDocument.Parse(raw);
                map = ToDictionary(doc.RootElement);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"template parse failed: {ex.Message}");
            return null;
        }

        string? Get(string key) =>
            map.TryGetValue(key, out var v) ? v?.ToString() : null;

        var sectionsJson = "[]";
        if (map.TryGetValue("sections", out var sect) && sect is not null)
        {
            sectionsJson = sect is string s ? s : JsonSerializer.Serialize(sect);
        }
        else if (map.TryGetValue("sectionsJson", out var sj) && sj is not null)
        {
            sectionsJson = sj.ToString() ?? "[]";
        }

        return new Dictionary<string, object?>
        {
            ["templateId"] = Get("templateId") ?? Get("id") ?? "",
            ["name"] = Get("name") ?? "",
            ["modality"] = Get("modality") ?? "",
            ["bodyPart"] = Get("bodyPart") ?? "",
            ["subspecialty"] = Get("subspecialty") ?? "",
            ["sectionsJson"] = sectionsJson,
        };
    }

    private static string TryString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static Dictionary<string, object?> ToDictionary(JsonElement el)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in el.EnumerateObject())
        {
            dict[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.TryGetInt64(out var n) ? (object)n : p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => p.Value.GetRawText(),
            };
        }
        return dict;
    }
}
