using RadioPad.Domain.Enums;

namespace RadioPad.Domain.Entities;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Tenant : Entity
{
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>If true, this tenant only allows providers with PHI-approved compliance class for PHI workflows.</summary>
    public bool RequirePhiApprovedProvider { get; set; } = true;
    /// <summary>
    /// PRD RB-010: when true, Draft / InReview rulebooks may still be used in
    /// AI runs (sandbox tenants). When false (default), only Approved
    /// rulebooks may be referenced from production clinical workflows.
    /// </summary>
    public bool AllowSandboxRulebooks { get; set; } = false;
    /// <summary>PRD MKT-002 — Stripe Connect Express account id used to
    /// receive marketplace payouts. Null until the publisher onboards.</summary>
    public string? StripeConnectAccountId { get; set; }
    /// <summary>
    /// Iter-31 MCP-005 — when false (default), the MCP tool registry rejects
    /// invocations of tools whose <c>Scope == External</c> regardless of
    /// approval. When true, an approved External tool may be called. This
    /// gate is independent of tenant-level RBAC and exists so a customer can
    /// disable all outbound MCP egress with a single switch.
    /// </summary>
    public bool AllowExternalMcp { get; set; } = false;
    public List<User> Users { get; set; } = new();
    public List<ProviderConfig> Providers { get; set; } = new();
}

public class User : Entity
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Radiologist;
    /// <summary>Hashed via ASP.NET Identity password hasher (dev only path).</summary>
    public string PasswordHash { get; set; } = "";
    /// <summary>PRD AUTH-005/AUTH-006 — soft-deprovisioning marker. SCIM PATCH
    /// `active:false` and emergency lockout flip this to false; sign-in and
    /// tenant-scoped queries must check it.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>PRD AUTH-003 — base32-encoded TOTP shared secret (RFC 6238).
    /// Empty when the user has not enrolled MFA. Treated as confidential and
    /// never echoed in API responses after enrollment confirmation.</summary>
    public string MfaSecret { get; set; } = "";
    /// <summary>True after the user successfully verifies their first TOTP
    /// code. Sign-in flows requiring MFA gate on this flag.</summary>
    public bool MfaEnabled { get; set; }
    /// <summary>
    /// Iter-32 AUTH-006 — count of failed credential attempts (TOTP, magic
    /// link consume, WebAuthn assertion, SAML/OIDC translation) inside the
    /// current rolling window. Resets on successful sign-in or after the
    /// window expires; once it reaches 5 the account is locked until
    /// <see cref="LockedUntil"/>.
    /// </summary>
    public int FailedLoginCount { get; set; }
    /// <summary>
    /// Iter-32 AUTH-006 — start of the current 15-minute counting window
    /// for <see cref="FailedLoginCount"/>. Null means no failures recorded
    /// in the current window.
    /// </summary>
    public DateTimeOffset? FailedLoginWindowStart { get; set; }
    /// <summary>
    /// Iter-32 AUTH-006 — when set, sign-in is rejected until this UTC
    /// instant. Auto-cleared on next successful sign-in or by an admin
    /// calling <c>POST /api/users/{id}/unlock</c>.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }
    /// <summary>
    /// Iter-32 AUTH-006 — incremented by <c>POST /api/users/{id}/revoke-sessions</c>.
    /// Bound into the HMAC seed of every minted bearer so previously issued
    /// tokens for this user become invalid as soon as the epoch increments.
    /// </summary>
    public int SessionEpoch { get; set; }
    /// <summary>
    /// Iter-35 i18n — optional per-user IETF locale tag override
    /// (e.g. <c>"en"</c>, <c>"es"</c>, <c>"de"</c>, <c>"fr"</c>,
    /// <c>"pt"</c>, <c>"hi"</c>). When null, the UI falls back to
    /// <see cref="TenantSettings.Locale"/>. Affects chrome only;
    /// clinical content (rulebooks, finding text, validation messages
    /// from the rulebook engine) is never translated.
    /// </summary>
    public string? PreferredLocale { get; set; }
}

public class ProviderConfig : Entity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Adapter id: "anthropic", "azure-openai", "openai-compatible", "gemini-cli", "mock", etc.</summary>
    public string Adapter { get; set; } = "mock";
    public string Model { get; set; } = "";
    public string EndpointUrl { get; set; } = "";
    /// <summary>Encrypted at rest in production; in dev held as opaque string.</summary>
    public string ApiKeySecretRef { get; set; } = "";
    public ProviderComplianceClass Compliance { get; set; } = ProviderComplianceClass.Sandbox;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;

    // PRD AI-010 cost-aware routing fields. Costs are USD per 1K tokens.
    public decimal CostPerInputKToken { get; set; } = 0m;
    public decimal CostPerOutputKToken { get; set; } = 0m;
    /// <summary>Hard cap per single call (USD). 0 = no cap.</summary>
    public decimal MaxCostPerCallUsd { get; set; } = 0m;

    /// <summary>
    /// Iter-32 AI-010 — operator-supplied quality score in <c>[0,1]</c>.
    /// Higher = better. Defaults to 0.5 so newly registered providers don't
    /// dominate routing until a human grades them. Used in the composite
    /// routing score (cost / quality / latency, weighted per-tenant).
    /// </summary>
    public decimal Quality { get; set; } = 0.5m;

    /// <summary>
    /// Iter-34 PROV-009 — operator-supplied free-text data-retention label
    /// shown alongside <see cref="Compliance"/>. Examples:
    /// <c>"no-egress"</c>, <c>"30d-soft-delete"</c>,
    /// <c>"vendor-controlled-zdr"</c>, <c>"baa-30d"</c>,
    /// <c>"local-only-no-retention"</c>. Informational only — never weakens
    /// the PHI policy enforced in <c>AiGateway.EnforcePhiPolicy</c>.
    /// </summary>
    public string RetentionLabel { get; set; } = "";

    // Iter-35 PROV-007 — OAuth refresh-token vault. The refresh token is
    // encrypted with AES-256-GCM under a fresh data-encryption key (DEK)
    // which is itself wrapped by the tenant's KMS-managed KEK. None of
    // these fields are returned to clients; the controller surface only
    // exposes a <c>hasToken</c> boolean and the timestamps. All four
    // crypto fields are populated together (atomically with SaveChangesAsync)
    // or all NULL when no token is stored.
    public byte[]? OAuthRefreshTokenEnc { get; set; }
    public byte[]? OAuthRefreshTokenIv { get; set; }
    public byte[]? OAuthRefreshTokenTag { get; set; }
    /// <summary>Per-token DEK wrapped under the tenant's KEK (KMS).</summary>
    public byte[]? OAuthRefreshTokenWrappedDek { get; set; }
    public DateTimeOffset? OAuthRefreshTokenUpdatedAt { get; set; }
    public DateTimeOffset? OAuthRefreshTokenExpiresAt { get; set; }
    /// <summary>"never" | "before_expiry" | "every_24h". Null = "before_expiry".</summary>
    public string? OAuthRefreshTokenRotationPolicy { get; set; }
}

/// <summary>
/// Tenant-scoped GitHub Copilot integration switchboard. Defaults are
/// intentionally fail-closed: disabled, no runtime enabled, no prompt/context
/// persistence, and no user-owned credentials unless an admin opts in.
/// Secret material is never stored here; only backend/vault references are.
/// </summary>
public class CopilotIntegrationSettings : Entity
{
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; }
    public bool EmergencyDisabled { get; set; } = true;
    public CopilotMode DefaultMode { get; set; } = CopilotMode.Disabled;
    /// <summary>Comma-separated <see cref="CopilotMode"/> names allowed by tenant policy.</summary>
    public string AllowedModes { get; set; } = "Disabled";
    public string GitHubEnterpriseSlug { get; set; } = "";
    public string GitHubOrganization { get; set; } = "";
    public string GitHubHost { get; set; } = "github.com";
    public bool SdkRuntimeEnabled { get; set; }
    public bool CliRuntimeEnabled { get; set; }
    public bool AllowByoAccounts { get; set; }
    public bool AllowEnvironmentTokenAuth { get; set; }
    public bool RequireOsKeychainForCli { get; set; } = true;
    public bool PromptLoggingEnabled { get; set; }
    public bool ContextLoggingEnabled { get; set; }
    public string RetentionPolicy { get; set; } = "metadata_only";
    public string PolicyJson { get; set; } =
        "{\"phi\":\"blocked\",\"promptLogging\":\"off\",\"contentStorage\":\"metadata_only\"}";

    // GitHub App / OAuth metadata. Secret refs are write-only at the API
    // boundary and should resolve to vault/KMS/env-backed storage.
    public string GitHubAppId { get; set; } = "";
    public string GitHubAppInstallationId { get; set; } = "";
    public string GitHubAppPrivateKeySecretRef { get; set; } = "";
    public string OAuthClientId { get; set; } = "";
    public string OAuthClientSecretRef { get; set; } = "";
    public DateTimeOffset? SecretsUpdatedAt { get; set; }
}

public class CopilotFeatureFlag : Entity
{
    public Guid TenantId { get; set; }
    public string FeatureKey { get; set; } = "";
    public bool Enabled { get; set; }
    public string RequiredRole { get; set; } = "";
    public string PolicyJson { get; set; } = "{}";
}

/// <summary>Token-free GitHub identity/eligibility snapshot for a RadioPad user.</summary>
public class CopilotUserAccount : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public CopilotMode Mode { get; set; } = CopilotMode.Disabled;
    public string GitHubLogin { get; set; } = "";
    public long? GitHubUserId { get; set; }
    public string TokenStatus { get; set; } = "none";
    public string TokenSecretRef { get; set; } = "";
    public string SsoStatus { get; set; } = "unknown";
    public string SeatStatus { get; set; } = "unknown";
    public string DenialReason { get; set; } = "runtime_not_configured";
    public DateTimeOffset? LastAuthenticatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Tenant/user eligibility snapshot derived from policy, CLI/OAuth state, SSO, seat, and quota gates.</summary>
public class CopilotEntitlement : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public CopilotMode Mode { get; set; } = CopilotMode.Disabled;
    public bool Allowed { get; set; }
    public string Source { get; set; } = "policy";
    public string GitHubLogin { get; set; } = "";
    public string SsoStatus { get; set; } = "unknown";
    public string SeatStatus { get; set; } = "unknown";
    public string DenialReason { get; set; } = "account_required";
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Request and concurrency limits for Copilot usage. ScopeKey is empty for tenant-wide policies.</summary>
public class CopilotQuotaPolicy : Entity
{
    public Guid TenantId { get; set; }
    public string ScopeType { get; set; } = "tenant";
    public string ScopeKey { get; set; } = "";
    public string Feature { get; set; } = "chat";
    public int WindowSeconds { get; set; } = 3600;
    public int MaxRequests { get; set; } = 20;
    public int MaxConcurrent { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}

/// <summary>Copilot conversation/session metadata. Prompt bodies are not persisted.</summary>
public class CopilotSession : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public CopilotMode Mode { get; set; } = CopilotMode.Disabled;
    public string Feature { get; set; } = "chat";
    public string ContextKind { get; set; } = "manual";
    public string Status { get; set; } = "created";
    public string Runtime { get; set; } = "";
    public string ContextHash { get; set; } = "";
    public string LastErrorKind { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

/// <summary>Message-level metadata for a Copilot session. Raw prompts and model output are excluded by default.</summary>
public class CopilotMessage : Entity
{
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "user";
    public int Sequence { get; set; }
    public string Status { get; set; } = "created";
    public string InputHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
    public string Model { get; set; } = "";
    public int LatencyMs { get; set; }
}

/// <summary>Metadata-only Copilot request ledger. Prompt/code bodies are not persisted.</summary>
public class CopilotUsageEvent : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string RequestId { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
    public string BlockKind { get; set; } = "";
    public int LatencyMs { get; set; }
    public string InputHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
}

public class CopilotDiagnosticRun : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "not_configured";
    public string ResultsJson { get; set; } = "{}";
}

public class Rulebook : Entity
{
    public Guid TenantId { get; set; }
    public string RulebookId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.0.1";
    public string Owner { get; set; } = "";
    public RulebookStatus Status { get; set; } = RulebookStatus.Draft;
    /// <summary>Raw YAML source (canonical).</summary>
    public string SourceYaml { get; set; } = "";
    /// <summary>Parsed JSON snapshot for fast querying.</summary>
    public string CompiledJson { get; set; } = "{}";
    public string? AppliesToModalities { get; set; }
    public string? AppliesToBodyParts { get; set; }
    /// <summary>
    /// Iter-31 RB-007 — optional department scope (e.g. "neuro", "msk",
    /// "cardiac"). When set, this rulebook is preferred for reports whose
    /// <see cref="Report.DepartmentTag"/> matches; otherwise falls through
    /// to a tenant-wide rulebook with the same id.
    /// </summary>
    public string? DepartmentTag { get; set; }
}

public class ReportTemplate : Entity
{
    public Guid TenantId { get; set; }
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Modality { get; set; } = "";
    public string BodyPart { get; set; } = "";
    public string Subspecialty { get; set; } = "";
    /// <summary>JSON describing sections and structured fields.</summary>
    public string SectionsJson { get; set; } = "[]";
    /// <summary>Iter-31 TMP-003 — variant flavour of the template.</summary>
    public TemplateVariant Variant { get; set; } = TemplateVariant.Normal;
    /// <summary>Iter-31 TMP-005 — approval lifecycle.</summary>
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    /// <summary>Iter-32 TMP-005 — user that approved this template (null until approved).</summary>
    public Guid? ApprovedBy { get; set; }
    /// <summary>Iter-32 TMP-005 — when the template was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
}

public class StudyContext
{
    public string AccessionNumber { get; set; } = "";
    public string Modality { get; set; } = "";
    public string BodyPart { get; set; } = "";
    public string Indication { get; set; } = "";
    public string Comparison { get; set; } = "";
    public string PriorReportSummary { get; set; } = "";
    public string PatientReference { get; set; } = "";
}

public class Report : Entity
{
    public Guid TenantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? RulebookId { get; set; }
    public Guid? TemplateId { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Draft;

    public StudyContext Study { get; set; } = new();

    public string Indication { get; set; } = "";
    public string Technique { get; set; } = "";
    public string Comparison { get; set; } = "";
    public string Findings { get; set; } = "";
    public string Impression { get; set; } = "";
    public string Recommendations { get; set; } = "";

    /// <summary>Section-keyed flag set: which sections currently contain unreviewed AI text.</summary>
    public string AiHighlightsJson { get; set; } = "{}";

    /// <summary>
    /// Iter-30 (Bidirectional FHIR) — when this report was created from an
    /// inbound FHIR <c>ServiceRequest</c>, this carries the resource
    /// reference (e.g. <c>"ServiceRequest/abc-123"</c>) so downstream
    /// systems can correlate the order with its eventual diagnostic report.
    /// </summary>
    public string? ServiceRequestRef { get; set; }

    /// <summary>
    /// Iter-31 RB-007 — optional department scope ("neuro", "msk", ...).
    /// Used by <c>ReportingService.ResolveRulebook*</c> to prefer a
    /// department-scoped rulebook over the tenant-wide fallback.
    /// </summary>
    public string? DepartmentTag { get; set; }

    public List<ReportVersion> Versions { get; set; } = new();
    public List<ReportSignature> Signatures { get; set; } = new();
}

public class ReportVersion : Entity
{
    public Guid ReportId { get; set; }
    public int Sequence { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid? RulebookId { get; set; }
    public string RulebookVersion { get; set; } = "";
    public Guid AuthorUserId { get; set; }
    public string Action { get; set; } = "edit";
    /// <summary>
    /// Iter-30 (Multi-radiologist sign-off + addendum) — when true, this
    /// version was appended via <c>POST /api/reports/{id}/addendum</c>
    /// after the report had already been signed at least once. The body
    /// content is in <see cref="SnapshotJson"/> under key <c>addendum</c>.
    /// </summary>
    public bool IsAddendum { get; set; }
}

/// <summary>
/// Iter-30 — a single radiologist signature on a report. A report may carry
/// at most one <see cref="SignatureRole.Primary"/> signature; additional
/// <see cref="SignatureRole.CoSigner"/> and <see cref="SignatureRole.Addendum"/>
/// rows are allowed. Append-only by convention; the integrity hash chains
/// each signature to its predecessor.
/// </summary>
public class ReportSignature : Entity
{
    public Guid ReportId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset SignedAt { get; set; } = DateTimeOffset.UtcNow;
    public SignatureRole Role { get; set; } = SignatureRole.Primary;
    public string? Note { get; set; }
    /// <summary>SHA-256 hash of <c>{id}|{reportId}|{userId}|{(int)role}|{signedAt:o}|{note}</c>.</summary>
    public string Hash { get; set; } = "";
}

public enum SignatureRole
{
    Primary = 0,
    CoSigner = 1,
    Addendum = 2,
}

public class AiRequest : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ReportId { get; set; }
    public string PromptVersion { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Mode { get; set; } = "draft";
    public bool ContainsPhi { get; set; }
    public string InputHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
    public int LatencyMs { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Status { get; set; } = "ok";
}

public class AuditEvent : Entity
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ReportId { get; set; }
    public AuditAction Action { get; set; }
    public string DetailsJson { get; set; } = "{}";
    /// <summary>Append-only enforcement: this is a hash of (Id|Action|DetailsJson|prev).</summary>
    public string IntegrityChain { get; set; } = "";
}

/// <summary>
/// Per-tenant terminology dictionary (PRD STD-006). Used by the validator to
/// flag forbidden abbreviations and suggest tenant-preferred replacements.
/// </summary>
public class TenantLexicon : Entity
{
    public Guid TenantId { get; set; }
    public string Term { get; set; } = "";
    /// <summary>If true, presence of <see cref="Term"/> is a Warning-level finding.</summary>
    public bool Forbidden { get; set; }
    public string Replacement { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>
/// Per-tenant settings (singleton row keyed by <see cref="TenantId"/>):
/// AI safety toggles (PRD AI-007), billing plan + feature flags
/// (PRD BILL-001/006), and Stripe customer linkage.
/// </summary>
public class TenantSettings : Entity
{
    public Guid TenantId { get; set; }

    // PRD AI-007 — deterministic hallucination / unsupported-claim detector.
    public bool HallucinationDetectionEnabled { get; set; } = true;    /// <summary>
    /// Severity when an unsupported claim is found in the impression. One of
    /// <see cref="ValidationSeverity"/> string names. Default = "Warning".
    /// </summary>
    public string HallucinationSeverity { get; set; } = "Warning";
    /// <summary>
    /// Newline- or comma-separated allow-list of clinical phrases that the
    /// detector should never flag (e.g. tenant-approved boilerplate).
    /// </summary>
    public string HallucinationAllowList { get; set; } = "";
    /// <summary>
    /// Minimum supporting-token overlap fraction (0.0 – 1.0) below which an
    /// impression sentence is considered unsupported. Default 0.3.
    /// </summary>
    public double HallucinationMinSupport { get; set; } = 0.3d;

    // PRD BILL-001/006 — plan + feature flags.
    public TenantPlan Plan { get; set; } = TenantPlan.Trial;
    /// <summary>JSON object: { "feature.x": true, ... }. Read by the API + UI.</summary>
    public string FeatureFlagsJson { get; set; } = "{}";

    // Stripe linkage. Secrets remain in env vars; only ids are stored.
    public string StripeCustomerId { get; set; } = "";
    public string StripeSubscriptionId { get; set; } = "";
    public string StripeSubscriptionStatus { get; set; } = "";
    public DateTimeOffset? StripeCurrentPeriodEnd { get; set; }

    // PRD BILL-001..006 — subscription lifecycle markers.
    /// <summary>Set when subscription is created in trial mode.</summary>
    public DateTimeOffset? TrialEndsAt { get; set; }
    /// <summary>Dunning deadline when subscription is past_due. After this, tenant is suspended.</summary>
    public DateTimeOffset? GracePeriodUntil { get; set; }
    /// <summary>Read-only mode flag; non-null = suspended.</summary>
    public DateTimeOffset? SuspendedAt { get; set; }
    /// <summary>Mirrors Stripe Connect `charges_enabled` (publisher account).</summary>
    public bool ChargesEnabled { get; set; } = false;
    /// <summary>Mirrors Stripe Connect `payouts_enabled` (publisher account).</summary>
    public bool PayoutsEnabled { get; set; } = false;

    // PRD INT-001..004 — inbound HL7/FHIR ingest webhook authentication.
    // Bearer token compared via constant-time equality. Empty = ingest disabled.
    public string IngestBearerSecret { get; set; } = "";

    /// <summary>
    /// Iter-31 INT-005 — optional HMAC-SHA256 secret used to verify the
    /// <c>X-RadioPad-Signature: sha256=&lt;hex&gt;</c> header on FHIR webhook
    /// calls (<c>POST /api/ingest/fhir/servicerequest</c> and
    /// <c>POST /api/ingest/fhir/diagnosticreport</c>). When empty, the
    /// existing bearer-only flow is used (back-compat). When set, the bearer
    /// AND the signature must both validate.
    /// </summary>
    public string FhirWebhookSecret { get; set; } = "";

    /// <summary>
    /// Iter-31 INT-006 — sending facility (MSH-4) used to map an inbound
    /// HL7 v2 MLLP ORU/ORM message to this tenant. Empty = HL7 v2 ingest is
    /// disabled for this tenant. Compared case-insensitively.
    /// </summary>
    public string Hl7SendingFacility { get; set; } = "";

    // PRD DCM-001..006 — DICOMweb (WADO-RS / QIDO-RS) base URL for
    // study-context lookup. Empty = disabled.
    public string DicomWebBaseUrl { get; set; } = "";
    /// <summary>Optional bearer token sent as Authorization on DICOMweb calls.</summary>
    public string DicomWebBearerSecret { get; set; } = "";

    /// <summary>
    /// Iter-33 INT-007 — vendor key selecting the active
    /// <see cref="RadioPad.Application.Services.Pacs.IPacsVendorAdapter"/>.
    /// One of <c>"sectra"</c>, <c>"visage"</c>, <c>"carestream"</c>, or
    /// <c>null</c>/empty meaning "unconfigured" (callers fall back to the
    /// generic DICOMweb client). Adapter credentials are read from
    /// vendor-specific environment variables — never stored in the
    /// database row.
    /// </summary>
    public string? PacsVendor { get; set; }

    // PRD §13.3 — data retention policy. Hash-only mode hides AI input/output
    // bodies from the audit log (only their SHA-256 is retained); legal hold
    // forbids any deletion regardless of retention window. Days <= 0 means
    // "retain indefinitely" (subject to platform-level limits).
    public int RetentionDays { get; set; } = 0;
    public bool HashOnlyAuditMode { get; set; } = false;
    public bool LegalHold { get; set; } = false;

    // PRD AUTH-005 — SCIM 2.0 bearer token for the IdP's provisioning agent.
    // Stored as the env-var name of the secret (e.g. "env:RADIOPAD_SCIM_BEARER")
    // OR the literal value in dev. Empty = SCIM disabled for this tenant.
    public string ScimBearerSecret { get; set; } = "";

    /// <summary>
    /// Iter-32 AUTH-005 — JSON map from SCIM Group <c>displayName</c>
    /// (case-insensitive) to <see cref="UserRole"/> name. Example:
    /// <c>{"radiologists": "Radiologist", "admins": "ReportingAdmin",
    /// "compliance": "ComplianceReviewer"}</c>. Group membership changes
    /// flowing in via SCIM PATCH/PUT are projected onto
    /// <see cref="User.Role"/> using this map (the highest-precedence
    /// matching group wins; when no membership matches, the user's role
    /// is left unchanged). Empty / invalid JSON disables projection.
    /// </summary>
    public string ScimGroupRoleMapJson { get; set; } = "{}";

    // PRD SEC-003 / Enterprise Plus — customer-managed encryption key (CMK).
    // RadioPad does NOT hold the master key in-process. The reference points
    // at the customer KMS/HSM (e.g. "aws-kms:arn:aws:kms:...:key/abc",
    // "azure-kv:https://vault.../keys/radiopad/v1", "gcp-kms:projects/.../cryptoKeys/k").
    // The application uses an envelope-encryption pattern: a per-tenant data
    // key is wrapped under this CMK reference and rotated by the customer.
    // Empty = platform-managed keys (default for SaaS).
    public string CmkKeyRef { get; set; } = "";
    /// <summary>Last time the customer attested that the CMK reference is
    /// reachable (set by the operator after a `keys verify` runbook step).</summary>
    public DateTimeOffset? CmkLastVerifiedAt { get; set; }

    // Iter-31 SEC-008 — per-tenant IP allowlist (comma-separated CIDR list).
    // ANDed with the global RADIOPAD_IP_ALLOWLIST envvar by IpAllowlistMiddleware.
    // Empty = no per-tenant restriction (defer to global allowlist if any).
    public string IpAllowlistCidr { get; set; } = "";

    /// <summary>
    /// Iter-32 SEC-008 — per-tenant IP allowlist as a JSON array of CIDR strings
    /// (IPv4 + IPv6), e.g. <c>["10.0.0.0/8","2001:db8::/32"]</c>. Stored as a JSON
    /// blob so the admin UI can edit it as one round-trip. Empty / null = fall back
    /// to <see cref="IpAllowlistCidr"/> for back-compat. ANDed with the global
    /// <c>RADIOPAD_IP_ALLOWLIST</c> envvar; loopback is always allowed.
    /// </summary>
    public string IpAllowlistJson { get; set; } = "";

    // Iter-31 RPT-012 / AI-007 — validation strictness toggles.
    /// <summary>
    /// When true (default), the export gate requires zero
    /// <see cref="Domain.Enums.ValidationSeverity.Blocker"/> findings in
    /// addition to <c>Status >= Validated</c>. When false, the radiologist
    /// has explicitly accepted residual blockers and exports proceed as long
    /// as the report has reached <c>Validated</c>.
    /// </summary>
    public bool RequireZeroBlockers { get; set; } = true;
    /// <summary>
    /// When true, every <see cref="Domain.Enums.ValidationSeverity.Warning"/>
    /// finding produced by <c>ReportValidator</c> is promoted to
    /// <see cref="Domain.Enums.ValidationSeverity.Blocker"/> before the
    /// result is returned. Default false (today's behaviour).
    /// </summary>
    public bool WarnAsBlocker { get; set; } = false;

    /// <summary>
    /// Iter-32 MCP-005 — when false (default), every MCP tool whose scope
    /// string contains <c>shell:</c>, <c>fs:</c>, or <c>net:</c> is rejected
    /// at runtime regardless of approval. When true AND the operator has
    /// also set <c>RADIOPAD_MCP_ALLOW_DANGEROUS=1</c> in the environment,
    /// dangerous-scope tools that have been approved may be invoked.
    /// </summary>
    public bool AllowDangerousMcp { get; set; } = false;

    /// <summary>
    /// Iter-32 AI-010 — JSON map of per-tenant routing weights for the
    /// composite cost / quality / latency score in <c>EfProviderRouter</c>.
    /// Schema: <c>{"cost": 0.5, "quality": 0.4, "latency": 0.1}</c>. Missing
    /// keys default to <c>1/3</c>; weights are normalised to sum to 1.0.
    /// </summary>
    public string RoutingWeightsJson { get; set; } = "{\"cost\":0.5,\"quality\":0.4,\"latency\":0.1}";

    /// <summary>
    /// Iter-35 i18n — tenant-level default UI locale as an IETF tag
    /// (one of <c>en</c>, <c>es</c>, <c>de</c>, <c>fr</c>, <c>pt</c>,
    /// <c>hi</c>). The frontend negotiates per-request locale from
    /// <c>?lang</c> → cookie → <c>Accept-Language</c> → this default →
    /// <c>en</c>. Affects chrome only; clinical rulebook text and
    /// validation messages from the rulebook engine are never
    /// translated.
    /// </summary>
    public string Locale { get; set; } = "en";
}

/// <summary>
/// PRD AUTH-004 � single-use magic-link token mailed to a user's address.
/// Stored hashed (SHA-256). Expires after 15 minutes; consumption flips
/// `ConsumedAt` so the row cannot be replayed.
/// </summary>
public class MagicLinkToken : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

/// <summary>
/// PRD AUTH-007 � RFC 8628 OAuth 2.0 Device Authorization Grant. The CLI
/// and desktop shells use this to pair without copy-pasting bearer tokens.
/// `UserCode` is shown to the human; `DeviceCodeHash` is what the device
/// polls with. Status: pending ? approved ? consumed | denied | expired.
/// </summary>
public class DeviceAuthRequest : Entity
{
    public string DeviceCodeHash { get; set; } = "";
    public string UserCode { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "pending";
    public int IntervalSeconds { get; set; } = 5;
    public DateTimeOffset? LastPolledAt { get; set; }
    /// <summary>
    /// Iter-31 AUTH-007 — opaque client-generated device fingerprint
    /// (hostname + OS + bundle id hash). Stored so admins can audit which
    /// physical device pairs hold a long-lived bearer and revoke them
    /// individually via <c>DELETE /api/devices/{id}</c>.
    /// </summary>
    public string? DeviceFingerprint { get; set; }
}

/// <summary>
/// Iter-31 AI-009 — per-tenant override for a named prompt block on a
/// specific rulebook. <c>ReportingService.BuildPromptForMode</c> consults
/// the override first, then the rulebook's own <c>prompt_blocks.&lt;mode&gt;</c>,
/// then the hard-coded clinically-conservative default. <see cref="RulebookId"/>
/// is the human-readable rulebook id (e.g. <c>chest_ct_v1</c>) — NOT the
/// EF row id — so an override survives rulebook re-imports.
/// </summary>
public class PromptOverride : Entity
{
    public Guid TenantId { get; set; }
    public string RulebookId { get; set; } = "";
    /// <summary>Prompt block key, e.g. "system", "impression", "draft", "dictation_cleanup", "follow_up".</summary>
    public string BlockKey { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>
    /// Iter-32 AI-009 — approval gate. New / edited rows land in
    /// <see cref="PromptOverrideStatus.Draft"/>; only
    /// <see cref="PromptOverrideStatus.Approved"/> rows are loaded by
    /// <c>EfPromptOverrideStore</c> at AI-runtime.
    /// </summary>
    public PromptOverrideStatus Status { get; set; } = PromptOverrideStatus.Draft;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}
/// <summary>
/// PRD MKT-001 \u2014 Marketplace listing for community-published rulebooks,
/// templates, or prompt packs. Lifecycle: Draft \u2192 Submitted \u2192 Approved
/// (or Rejected) \u2192 Deprecated. Pricing in USD cents; Stripe Connect
/// revenue share is settled via the platform Stripe account on purchase.
/// </summary>
public class MarketplaceListing : Entity
{
    public Guid PublisherTenantId { get; set; }
    public Guid PublisherUserId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>"rulebook" | "template" | "prompt_pack".</summary>
    public string Kind { get; set; } = "rulebook";
    /// <summary>Artifact body (YAML or JSON) of the published asset.</summary>
    public string ArtifactBody { get; set; } = "";
    /// <summary>Price in USD cents. Zero means free.</summary>
    public int PriceCents { get; set; }
    /// <summary>"draft" | "submitted" | "approved" | "rejected" | "deprecated".</summary>
    public string Status { get; set; } = "draft";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewerUserId { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>Cached Stripe Price id created on approval (null for free items).</summary>
    public string? StripePriceId { get; set; }
    /// <summary>Revenue share to publisher in basis points (e.g. 7000 = 70%).</summary>
    public int RevenueShareBps { get; set; } = 7000;
    /// <summary>PRD Enterprise GA #13 — source rulebook id when category is "rulebook".</summary>
    public string? SourceRulebookId { get; set; }
    /// <summary>PRD Enterprise GA #13 — source template id when category is "template".</summary>
    public string? SourceTemplateId { get; set; }
    /// <summary>PRD Enterprise GA #13 — semver of the published content.</summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>PRD Enterprise GA #13 — number of tenant installs.</summary>
    public int InstallCount { get; set; }
    /// <summary>PRD Enterprise GA #13 — reviewer feedback (approval or rejection notes).</summary>
    public string? ReviewNotes { get; set; }
}

/// <summary>
/// PRD MKT-005 \u2014 record of a marketplace purchase. Created in `pending`
/// when a Stripe Checkout session is opened; flipped to `paid` from the
/// Stripe webhook callback.
/// </summary>
public class MarketplacePurchase : Entity
{
    public Guid ListingId { get; set; }
    public Guid BuyerTenantId { get; set; }
    public Guid BuyerUserId { get; set; }
    public int AmountCents { get; set; }
    public string Status { get; set; } = "pending";
    public string? StripeSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>
/// PRD BILL-006 — Stripe webhook event de-duplication / replay-protection
/// table. Persisted on first receipt of a Stripe `Event.Id` for a source;
/// subsequent deliveries of the same id to the same source are no-ops.
/// Untenanted (Stripe events don't carry our tenant id directly; we resolve
/// via customer/subscription).
/// </summary>
public class StripeWebhookEvent : Entity
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Source { get; set; } = "";  // "billing" | "marketplace"
}

/// <summary>
/// PRD MOB-007 — registered mobile push device for a (tenant, user). Tokens
/// are unique APNs device tokens (iOS) or FCM registration tokens (Android).
/// Web platform rows are accepted for parity but are no-ops on the sender.
/// </summary>
public class PushDevice : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>"ios" | "android" | "web".</summary>
    public string Platform { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Iter-31 MCP-001/002/005 — Model Context Protocol tool registration. Each
/// row is a single tool that a tenant has registered for use by their
/// radiologists' MCP clients (CLI / desktop / agentic IDE). Lifecycle:
/// draft (Approved=false) → approved (admin flips Approved=true) → revoked
/// (admin flips Approved=false again with audit). External-scope tools are
/// additionally gated by <see cref="Tenant.AllowExternalMcp"/>.
/// </summary>
public class McpTool : Entity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>BuiltIn = ships with RadioPad. Custom = tenant-authored.</summary>
    public McpToolKind Kind { get; set; } = McpToolKind.BuiltIn;
    /// <summary>ReadOnly = no side effects. ReadWrite = mutates RadioPad state. External = reaches outside the tenant boundary.</summary>
    public McpToolScope Scope { get; set; } = McpToolScope.ReadOnly;
    public bool Approved { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    /// <summary>Newline- or comma-separated regex patterns the sandbox is allowed to match for outbound connector calls. Empty = no connectors.</summary>
    public string AllowedConnectorPaths { get; set; } = "";

    // Iter-32 MCP-001/002/007 — least-privilege manifest fields.
    /// <summary>Tool semver (e.g. "1.2.0"). Defaults to "1.0.0".</summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>Comma-separated least-privilege scope tokens (e.g. "net:dicomweb,fs:read"). Empty for built-in safe tools.</summary>
    public string ScopeString { get; set; } = "";
    /// <summary>Full JSON manifest as submitted (canonical form is hashed below).</summary>
    public string ManifestJson { get; set; } = "";
    /// <summary>Lower-case hex SHA-256 of <see cref="ManifestJson"/>.</summary>
    public string ManifestSha256 { get; set; } = "";
    /// <summary>Detached Ed25519 signature over <see cref="ManifestJson"/> bytes (b64). Empty for unsigned tenant-authored tools.</summary>
    public string ManifestSig { get; set; } = "";
    /// <summary>Lifecycle: Submitted → Approved → Blocked. Drives runtime gate.</summary>
    public McpToolStatus Status { get; set; } = McpToolStatus.Submitted;
    /// <summary>True iff the tool ships with RadioPad (signed by the release key). False = tenant-authored.</summary>
    public bool IsBuiltIn { get; set; }
}

public enum McpToolKind
{
    BuiltIn = 0,
    Custom = 1,
}

public enum McpToolScope
{
    ReadOnly = 0,
    ReadWrite = 1,
    External = 2,
}

/// <summary>Iter-32 MCP-001/002 — registry lifecycle for an <see cref="McpTool"/>.</summary>
public enum McpToolStatus
{
    Submitted = 0,
    Approved = 1,
    Blocked = 2,
}

/// <summary>
/// Iter-31 MCP-004 — append-only invocation ledger. Bodies are never persisted;
/// only their SHA-256 hashes are retained so audits can prove that a given
/// input/output pair was processed without retaining PHI in the log.
/// </summary>
public class McpToolCall : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid ToolId { get; set; }
    public Guid? ReportId { get; set; }
    public string InputHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
    /// <summary>"ok" | "blocked" | "error" | "timeout".</summary>
    public string Status { get; set; } = "ok";

    // Iter-32 MCP-004 — denormalised columns to make the invocation ledger
    // useful without joining to <see cref="McpTool"/> (which may have been
    // renamed or deleted).
    public string ToolName { get; set; } = "";
    public string ScopeString { get; set; } = "";
    public int LatencyMs { get; set; }
}

/// <summary>
/// Iter-32 AUTH-005 — SCIM 2.0 Group resource (RFC 7643 §4.2). Represents
/// an IdP-managed group that, via <see cref="TenantSettings.ScimGroupRoleMapJson"/>,
/// projects onto a RadioPad <see cref="UserRole"/>. Members are tracked in
/// <see cref="ScimGroupMembership"/>.
/// </summary>
public class ScimGroup : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>SCIM <c>displayName</c>. Used as the lookup key in the role map.</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Optional <c>externalId</c> echoed by the IdP. Stored verbatim.</summary>
    public string? ExternalId { get; set; }
    public List<ScimGroupMembership> Members { get; set; } = new();
}

/// <summary>
/// Iter-32 AUTH-005 — link row between <see cref="ScimGroup"/> and
/// <see cref="User"/>. Composite uniqueness: (TenantId, GroupId, UserId).
/// </summary>
public class ScimGroupMembership : Entity
{
    public Guid TenantId { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>
/// Iter-32 AUTH-001 — registered WebAuthn / FIDO2 passkey credential for a
/// user. The public key is stored verbatim (CBOR-encoded COSE_Key, base64);
/// <see cref="CredentialIdHash"/> is the SHA-256 hex of the raw credential
/// id so unique-index enforcement does not require persisting the raw id.
/// <see cref="SignCount"/> is monotonically updated on every successful
/// assertion to detect cloned authenticators.
/// </summary>
public class WebAuthnCredential : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Raw credential id as base64url (returned by the authenticator).</summary>
    public string CredentialId { get; set; } = "";
    /// <summary>SHA-256 hex of the raw credential id (lookup + dedup).</summary>
    public string CredentialIdHash { get; set; } = "";
    /// <summary>Base64-encoded COSE_Key public key from the attestation statement.</summary>
    public string PublicKey { get; set; } = "";
    /// <summary>Counter most recently observed in an authenticator assertion.</summary>
    public uint SignCount { get; set; }
    /// <summary>Optional human label set at registration ("YubiKey 5C", "iCloud Keychain").</summary>
    public string Label { get; set; } = "";
    /// <summary>WebAuthn attestation format ("none", "packed", "fido-u2f") verified at registration.</summary>
    public string AttestationFormat { get; set; } = "";
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// Iter-33 MCP-007 — per-tenant ed25519 publisher key trusted to sign
/// plugin <c>manifest.json</c> bodies. The
/// <see cref="Application.Services.Mcp.PluginManifestSignatureVerifier"/>
/// rejects any plugin whose detached <c>manifest.json.sig</c> does not
/// verify against an active row in this table. Rows are append-only:
/// rotating a key adds a new row; revoking sets <see cref="RevokedAt"/>
/// without deleting so we keep the audit history of what used to be
/// trusted.
/// </summary>
public class TrustedPluginPublisher : Entity
{
    public Guid TenantId { get; set; }
    public string PublisherName { get; set; } = "";
    /// <summary>Raw 32-byte ed25519 public key, base64-encoded.</summary>
    public string Ed25519PublicKeyBase64 { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Iter-35 � versioned, tenant-scoped bundle of golden test cases that a
/// rulebook must pass before promotion. The on-disk format mirrors the
/// existing fixtures under <c>rulebooks/_tests/&lt;rulebook_id&gt;/*.json</c>;
/// this row carries the management surface (lifecycle, audit, RBAC).
/// Lifecycle: Draft -> Approved (or Deprecated). Approval is a clinical-
/// governance gate; deprecation does not block running the suite, only
/// signals the pack should not be used to certify new rulebook versions.
/// </summary>
public class ValidationPack : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>Human-readable rulebook id (snake_case), e.g. <c>chest_ct_v1</c>.</summary>
    public string RulebookId { get; set; } = "";
    /// <summary>Pack version (semver).</summary>
    public string Version { get; set; } = "0.1.0";
    public string Name { get; set; } = "";
    public ValidationPackStatus Status { get; set; } = ValidationPackStatus.Draft;
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public Guid CreatedBy { get; set; }
    /// <summary>JSON array of <c>{name, report, expectFlagged}</c> objects.</summary>
    public string GoldenCasesJson { get; set; } = "[]";
}

/// <summary>
/// PRD §18.2 — last known-good golden-case regression result for a
/// (tenant, provider, rulebook) triple. Updated by
/// <c>ModelDriftDetectionService</c> whenever a check produces a passing
/// (no-drift) result. The stored quality score and finding-rule snapshot
/// serve as the comparison baseline for the next scheduled run.
/// </summary>
public class DriftBaseline : Entity
{
    public Guid TenantId { get; set; }
    public string ProviderId { get; set; } = "";
    public string RulebookId { get; set; } = "";
    public int QualityScore { get; set; }
    /// <summary>JSON array of rule IDs that fired in the baseline run.</summary>
    public string FindingRuleIdsJson { get; set; } = "[]";
    public DateTimeOffset CheckedAt { get; set; }
}
