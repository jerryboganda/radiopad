using System.Text.Json;

namespace RadioPad.Application.Services;

/// <summary>
/// Shared parser that turns a model's section-keyed JSON response into a
/// case-insensitive section map. Used by both the dictation-cleanup pipeline
/// (<see cref="DictationCleanupService"/>) and the full-report generation
/// pipeline (<see cref="ReportingService.GenerateStructuredAsync"/>). Centralised
/// so the JSON-fence stripping and the free-text fallback stay identical across
/// both AI paths.
/// </summary>
public static class ReportSectionJson
{
    /// <summary>
    /// Parse <paramref name="body"/> into a section map, tolerating the wrappers
    /// that web-scraped model output arrives in:
    /// <list type="bullet">
    ///   <item>a <c>```json … ```</c> code fence;</item>
    ///   <item>a bare language-label line — e.g. <c>JSON\n{ … }</c> — which is what
    ///   the UBAG gateway produces when its DOM scraper flattens a <c>```json</c>
    ///   block from the Gemini web UI into the language tag plus the fenced body;</item>
    ///   <item>stray prose before/after the object (last resort: the outermost
    ///   <c>{ … }</c> span is extracted and parsed).</item>
    /// </list>
    /// Every string property of the root object is read. When nothing parses, the
    /// whole body is surfaced under <c>findings</c> so the radiologist still sees
    /// something useful and the remaining sections are left untouched by the caller.
    /// </summary>
    public static Dictionary<string, string> Parse(string? body)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body)) return map;
        var trimmed = body.Trim();

        // Strip a code fence if the model wrapped JSON in ```json ... ``` (the
        // first line may carry the language tag; drop the whole line).
        if (trimmed.StartsWith("```"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        // Strip a bare leading language-label line (e.g. "json" / "JSON") left by a
        // scraper that dropped the fence backticks but kept the language tag. Only
        // when the remainder actually begins a JSON object, so genuine prose that
        // merely starts with a short word is untouched.
        if (trimmed.Length > 0 && trimmed[0] is not ('{' or '['))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0)
            {
                var firstLine = trimmed[..nl].Trim();
                var rest = trimmed[(nl + 1)..].TrimStart();
                if (rest.StartsWith("{") && IsBareLabel(firstLine))
                    trimmed = rest;
            }
        }

        if (TryReadObject(trimmed, map)) return map;

        // Last resort before free-text fallback: carve out the outermost { ... }
        // span and parse that, discarding any surrounding prose the model added.
        var open = trimmed.IndexOf('{');
        var close = trimmed.LastIndexOf('}');
        if (open >= 0 && close > open && TryReadObject(trimmed[open..(close + 1)], map))
            return map;

        // Model returned free text — surface it as the Findings section so the
        // radiologist can still see something useful, but leave the remaining
        // sections empty so the caller does not overwrite them.
        map["findings"] = body;
        return map;
    }

    /// <summary>A short, spaceless word such as <c>json</c> — a plausible dropped
    /// code-fence language tag, never real report prose.</summary>
    private static bool IsBareLabel(string line) =>
        line.Length is > 0 and <= 12 && !line.Contains(' ') && line.All(char.IsLetter);

    /// <summary>Parse <paramref name="candidate"/> as a JSON object and copy its
    /// string properties into <paramref name="map"/>. Returns false (leaving the
    /// map untouched) when the text is not a valid JSON object.</summary>
    private static bool TryReadObject(string candidate, Dictionary<string, string> map)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
