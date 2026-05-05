namespace RadioPad.Application.Services;

/// <summary>
/// PRD §security — canonical reader for Stripe configuration. Canonical scheme
/// is `RADIOPAD_STRIPE_*`; we read the canonical name first and fall back to
/// the legacy `STRIPE_*` form for one release so existing operator pipelines
/// keep working. Returns null if neither is set.
/// </summary>
public static class BillingEnv
{
    public static string? SecretKey => Read("RADIOPAD_STRIPE_SECRET_KEY", "STRIPE_SECRET_KEY");
    public static string? WebhookSecret => Read("RADIOPAD_STRIPE_WEBHOOK_SECRET", "STRIPE_WEBHOOK_SECRET");

    public static string? Read(string canonical, string legacy)
    {
        var v = Environment.GetEnvironmentVariable(canonical);
        if (!string.IsNullOrWhiteSpace(v)) return v;
        v = Environment.GetEnvironmentVariable(legacy);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
