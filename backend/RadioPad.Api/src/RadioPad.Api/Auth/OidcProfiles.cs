namespace RadioPad.Api.Auth;

/// <summary>
/// PRD AUTH-001 / INT-001 — concrete OIDC presets for the three IdPs that
/// RadioPad ships ready-to-go integrations for: Keycloak, Auth0, and Okta.
///
/// The presets are pure documentation + env-var defaults. The bearer
/// pipeline is still implemented by <c>OidcBearerMiddleware</c>, which
/// reads <c>RADIOPAD_OIDC_*</c> env vars at request time. When
/// <c>RADIOPAD_OIDC_PRESET</c> is set to one of <c>keycloak</c>,
/// <c>auth0</c>, or <c>okta</c>, the operator only needs to supply the
/// IdP-specific authority/audience values; this class fills in the
/// remaining defaults (claim mappings, MFA enforcement) so a typo in the
/// claim name does not silently disable SSO.
///
/// Operator quick reference:
/// <list type="bullet">
/// <item>
///   <c>keycloak</c> — set <c>RADIOPAD_OIDC_AUTHORITY</c> to
///   <c>https://kc.example/realms/{realm}</c>, <c>RADIOPAD_OIDC_AUDIENCE</c>
///   to your client id, and add a <c>tenant_slug</c> claim mapper.
/// </item>
/// <item>
///   <c>auth0</c> — set <c>RADIOPAD_OIDC_AUTHORITY</c> to
///   <c>https://{your-tenant}.auth0.com/</c> and <c>RADIOPAD_OIDC_AUDIENCE</c>
///   to your API audience. Tenant lives in <c>https://radiopad/tenant_slug</c>
///   custom claim by default.
/// </item>
/// <item>
///   <c>okta</c> — set <c>RADIOPAD_OIDC_AUTHORITY</c> to
///   <c>https://{org}.okta.com/oauth2/{server}</c>, audience to your
///   resource server. Tenant lives in <c>tenant_slug</c> claim configured
///   on the authorization server.
/// </item>
/// </list>
///
/// Secrets (client secrets, signing keys) are <em>never</em> baked into
/// presets — RadioPad uses the public key endpoint advertised by the IdP's
/// discovery document (<c>/.well-known/openid-configuration</c>).
/// </summary>
public static class OidcProfiles
{
    public sealed record Profile(
        string Name,
        string DefaultTenantClaim,
        string DefaultEmailClaim,
        bool DefaultRequireMfa,
        string? AmrMfaValueHint,
        string OperatorNotes);

    private static readonly Profile Keycloak = new(
        Name: "keycloak",
        DefaultTenantClaim: "tenant_slug",
        DefaultEmailClaim: "email",
        DefaultRequireMfa: true,
        AmrMfaValueHint: "mfa",
        OperatorNotes: "Add a 'User Attribute' protocol mapper named 'tenant_slug' on the realm client; check 'Add to ID token' and 'Add to access token'.");

    private static readonly Profile Auth0 = new(
        Name: "auth0",
        DefaultTenantClaim: "https://radiopad/tenant_slug",
        DefaultEmailClaim: "email",
        DefaultRequireMfa: true,
        AmrMfaValueHint: "mfa",
        OperatorNotes: "Use an Auth0 Action to add 'tenant_slug' as a custom claim under the 'https://radiopad/' namespace; enable MFA enrollment for the connection.");

    private static readonly Profile Okta = new(
        Name: "okta",
        DefaultTenantClaim: "tenant_slug",
        DefaultEmailClaim: "email",
        DefaultRequireMfa: true,
        AmrMfaValueHint: "mfa",
        OperatorNotes: "Add a custom claim 'tenant_slug' on the authorization server with expression 'user.tenant_slug'; require MFA via the Okta sign-on policy.");

    private static readonly IReadOnlyDictionary<string, Profile> All =
        new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase)
        {
            ["keycloak"] = Keycloak,
            ["auth0"] = Auth0,
            ["okta"] = Okta,
        };

    public static IReadOnlyCollection<Profile> Known => All.Values.ToList();

    public static Profile? Resolve(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : All.TryGetValue(name, out var p) ? p : null;

    /// <summary>
    /// Apply the named preset to the process environment so
    /// <c>OidcBearerMiddleware</c> picks it up. Existing
    /// <c>RADIOPAD_OIDC_*</c> env vars are <em>not</em> overwritten — the
    /// operator's explicit values always win.
    /// </summary>
    public static Profile? ApplyToEnvironment(string? name)
    {
        var profile = Resolve(name);
        if (profile is null) return null;
        SetIfMissing("RADIOPAD_OIDC_TENANT_CLAIM", profile.DefaultTenantClaim);
        SetIfMissing("RADIOPAD_OIDC_EMAIL_CLAIM", profile.DefaultEmailClaim);
        if (profile.DefaultRequireMfa) SetIfMissing("RADIOPAD_OIDC_REQUIRE_MFA", "1");
        return profile;
    }

    private static void SetIfMissing(string key, string value)
    {
        var existing = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(existing)) Environment.SetEnvironmentVariable(key, value);
    }
}
