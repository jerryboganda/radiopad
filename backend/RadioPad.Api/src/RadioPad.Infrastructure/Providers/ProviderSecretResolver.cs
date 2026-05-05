namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Resolves opaque <c>ApiKeySecretRef</c> values to concrete secret material.
/// Convention:
/// <list type="bullet">
///   <item><c>env:NAME</c> &#8594; <c>Environment.GetEnvironmentVariable("NAME")</c></item>
///   <item><c>{empty}</c> &#8594; falls back to <paramref name="fallbackEnv"/> when supplied</item>
///   <item>anything else &#8594; unresolved; literal provider secrets are not accepted</item>
/// </list>
/// Provider API keys never appear in JSON responses, logs, or audit details
/// (security boundary §secrets).
/// </summary>
internal static class ProviderSecretResolver
{
    public static string? Resolve(string? secretRef, string? fallbackEnv = null)
    {
        if (string.IsNullOrEmpty(secretRef))
            return string.IsNullOrEmpty(fallbackEnv) ? null : Environment.GetEnvironmentVariable(fallbackEnv);
        if (secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(secretRef[4..]);
        return string.IsNullOrEmpty(fallbackEnv) ? null : Environment.GetEnvironmentVariable(fallbackEnv);
    }
}
