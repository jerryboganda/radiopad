namespace RadioPad.Infrastructure.Pacs;

/// <summary>
/// Iter-33 INT-007 — minimal secret-reference resolver shared by the
/// vendor PACS adapters. Mirrors <c>ProviderSecretResolver</c> in shape
/// but is colocated under <c>RadioPad.Infrastructure.Pacs</c> to keep
/// the adapters' dependency surface narrow.
///
/// Supported reference schemes:
/// <list type="bullet">
///   <item><c>env:NAME</c> &#8594; <c>Environment.GetEnvironmentVariable("NAME")</c>.</item>
///   <item>empty string &#8594; falls back to <paramref name="fallbackEnv"/>.</item>
///   <item>other schemes/literals &#8594; unresolved; inline PACS secrets are not accepted.</item>
/// </list>
/// Cloud-secret-manager schemes (<c>aws:</c>, <c>azkv:</c>, <c>gcp:</c>)
/// are reserved for a future iteration; the corresponding KMS adapters
/// already exist for envelope encryption but secret-fetch is a separate
/// surface and is intentionally not implemented here.
/// </summary>
internal static class PacsSecretResolver
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
