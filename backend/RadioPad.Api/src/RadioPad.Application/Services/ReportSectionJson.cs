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
    /// Parse <paramref name="body"/> into a section map. Strips a leading
    /// <c>```json</c> (or bare <c>```</c>) fence when the model wraps its JSON,
    /// then reads every string property of the root object. When the response is
    /// not valid JSON the whole body is surfaced under <c>findings</c> so the
    /// radiologist still sees something useful and the remaining sections are
    /// left untouched by the caller.
    /// </summary>
    public static Dictionary<string, string> Parse(string? body)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body)) return map;
        var trimmed = body.Trim();

        // Strip code fences if the model wrapped JSON in ```json ... ```
        if (trimmed.StartsWith("```"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Model returned free text — surface it as the Findings section so
            // the radiologist can still see something useful, but leave the
            // remaining sections empty so the caller does not overwrite them.
            map["findings"] = body;
        }
        return map;
    }
}
