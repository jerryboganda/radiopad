using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-008 — defence-in-depth client-side PHI guard. Before any AI command
/// runs, fetch the report + provider and refuse locally when the report
/// is flagged <c>containsPhi</c> and the provider's compliance class is
/// neither <c>PhiApproved</c> nor <c>LocalOnly</c>. The server still
/// enforces the policy authoritatively in <c>AiGateway</c>.
/// </summary>
public static class PhiGuard
{
    /// <summary>
    /// Returns <c>0</c> when the request is allowed (or cannot be evaluated
    /// — fail-open here is safe because the server is authoritative), or
    /// <see cref="CliRuntime.ExitPhiPolicyBlocked"/> when the local check
    /// refuses.
    /// </summary>
    public static async Task<int> EnsureAllowedAsync(HttpClient http, string reportId, Guid? providerId, CancellationToken ct)
    {
        if (providerId is null || providerId == Guid.Empty) return 0; // auto-routing — server picks compliant provider.

        bool containsPhi;
        try
        {
            var rs = await http.GetStringAsync($"/api/reports/{reportId}", ct);
            using var rd = JsonDocument.Parse(rs);
            containsPhi = ContainsPhi(rd.RootElement);
        }
        catch
        {
            return 0; // server still enforces.
        }
        if (!containsPhi) return 0;

        string compliance;
        try
        {
            var ps = await http.GetStringAsync("/api/providers", ct);
            using var pd = JsonDocument.Parse(ps);
            compliance = "";
            foreach (var p in pd.RootElement.EnumerateArray())
            {
                if (p.TryGetProperty("id", out var id) && id.GetString()?.Equals(providerId.Value.ToString(), StringComparison.OrdinalIgnoreCase) == true)
                {
                    compliance = p.TryGetProperty("compliance", out var c)
                        ? (c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.GetRawText().Trim('"'))
                        : "";
                    break;
                }
            }
        }
        catch
        {
            return 0;
        }
        if (string.Equals(compliance, "PhiApproved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(compliance, "LocalOnly", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        Console.Error.WriteLine($"phi-policy-blocked: provider compliance={compliance} cannot receive PHI for report {reportId}.");
        return CliRuntime.ExitPhiPolicyBlocked;
    }

    /// <summary>
    /// Determines whether the report (or its enclosing JSON envelope)
    /// looks PHI-bearing. We honour an explicit <c>containsPhi</c> flag
    /// when the API exposes it; otherwise fall back to a non-empty
    /// <c>study.patientReference</c> as a conservative proxy.
    /// </summary>
    public static bool ContainsPhi(JsonElement report)
    {
        if (report.TryGetProperty("containsPhi", out var cp) && cp.ValueKind == JsonValueKind.True) return true;
        if (report.TryGetProperty("study", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            if (st.TryGetProperty("patientReference", out var pr) && pr.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(pr.GetString())) return true;
        }
        return false;
    }
}
