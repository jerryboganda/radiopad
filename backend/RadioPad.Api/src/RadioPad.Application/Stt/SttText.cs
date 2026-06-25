namespace RadioPad.Application.Stt;

/// <summary>Helpers for turning an engine's free-text output into word tokens.</summary>
public static class SttText
{
    /// <summary>
    /// Split free text into whitespace-delimited word tokens, each carrying
    /// <paramref name="confidence"/>. Empty entries are dropped.
    /// </summary>
    public static IReadOnlyList<SttToken> Tokenize(string? text, double confidence)
        => (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new SttToken(w, confidence))
            .ToList();
}
