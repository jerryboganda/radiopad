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
    /// <summary>
    /// AUTH-003 — when true (default), every user must enrol a TOTP authenticator
    /// and complete the second factor on each password sign-in. The password
    /// sign-in path forces first-login enrolment regardless; this flag exists so
    /// the policy is explicit and a future tenant could relax it.
    /// </summary>
    public bool RequireMfa { get; set; } = true;
    /// <summary>
    /// PR-N2 — opt-in stale-draft auto-archive window in days. 0 (default) disables the
    /// weekly <c>OrphanedDraftCleanupJob</c> for this tenant; when &gt; 0, Draft reports
    /// untouched for longer than this many days are soft-archived (see
    /// <see cref="Report.ArchivedAt"/>).
    /// </summary>
    public int DraftAutoArchiveDays { get; set; } = 0;
    /// <summary>
    /// PR-N2 — CSV of notification categories this tenant treats as critical (default
    /// <c>"CriticalResult"</c>). Reserved for the later notification producers; carried on
    /// the tenant so the platform tables land together.
    /// </summary>
    public string CriticalNotificationCategoriesCsv { get; set; } = "CriticalResult";
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

/// <summary>
/// Enterprise identity root. It is account metadata only; tenant authorization
/// continues to resolve through <see cref="TenantMembership"/> and the legacy
/// tenant-scoped <see cref="User"/> row.
/// </summary>
public class GlobalUser : Entity
{
    public string PrimaryEmail { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// Stable external login subject linked to a global account. Email is stored
/// as a snapshot only; provider + issuer + subject is the unique identifier.
/// </summary>
public class ExternalIdentity : Entity
{
    public Guid GlobalUserId { get; set; }
    public string ProviderKey { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ClaimsJson { get; set; } = "{}";
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Bridge from global identity into the existing tenant-local user model. This
/// slice keeps <see cref="User"/> authoritative for role, lockout, and clinical
/// actor ids.
/// </summary>
public class TenantMembership : Entity
{
    public Guid GlobalUserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "active";
    public bool IsDefault { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
    public int SessionEpoch { get; set; }
}

/// <summary>
/// Durable inventory row for issued RadioPad sessions. Token material is never
/// stored; <see cref="TokenHash"/> is a one-way digest.
/// </summary>
public class AuthSession : Entity
{
    public Guid GlobalUserId { get; set; }
    public Guid? TenantMembershipId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Method { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string RevocationReason { get; set; } = "";
    public string DeviceFingerprintHash { get; set; } = "";
    public string IpHash { get; set; } = "";
    public string UserAgentHash { get; set; } = "";
    public int SessionEpochAtIssue { get; set; }
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
    /// <summary>
    /// Hybrid contrast model — the contrast phase this template is written for.
    /// "" (default) = contrast-agnostic: matches any selection. Otherwise one of
    /// "None" | "With" | "WithAndWithout". Influences TEMPLATE resolution only
    /// (rulebooks stay keyed on Modality+BodyPart) — see
    /// <c>ReportingService.ResolveBindings</c>'s 3-tier contrast preference.
    /// </summary>
    public string Contrast { get; set; } = "";
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

/// <summary>
/// Admin-managed catalog of imaging modalities (CT, MR, US, ...). Tenant-scoped
/// so each org curates its own list. Together with <see cref="BodyPart"/> this is
/// the single selection key that drives report-template and rulebook (prompt)
/// resolution — see <c>ReportingService.ResolveBindingsAsync</c>.
/// </summary>
public class Modality : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>Stable code used for matching (e.g. "CT"). Unique per tenant.</summary>
    public string Code { get; set; } = "";
    /// <summary>Human-readable label (e.g. "Computed Tomography"). Falls back to Code when blank.</summary>
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Admin-managed catalog of anatomical regions (Chest, Abdomen, ...). Tenant-scoped.
/// See <see cref="Modality"/> for how the pair drives template/rulebook resolution.
/// </summary>
public class BodyPart : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>Stable code used for matching (e.g. "Chest"). Unique per tenant.</summary>
    public string Code { get; set; } = "";
    /// <summary>Human-readable label. Falls back to Code when blank.</summary>
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

public class StudyContext
{
    public string AccessionNumber { get; set; } = "";
    public string Modality { get; set; } = "";
    public string BodyPart { get; set; } = "";
    /// <summary>
    /// Hybrid contrast model — the selected contrast phase for this study.
    /// "" = unspecified; otherwise "None" | "With" | "WithAndWithout". Free-form
    /// string (mirrors Modality/BodyPart/Gender), matched case-insensitively; the
    /// UI constrains it to the fixed set. Drives contrast-aware template resolution.
    /// </summary>
    public string Contrast { get; set; } = "";
    /// <summary>Iter-36 — patient age in years. Null when unknown. Replaces the
    /// former study-context Indication field; the report-body Indication section
    /// (<see cref="Report.Indication"/>) remains the clinical indication of record.</summary>
    public int? Age { get; set; }
    /// <summary>Iter-36 — patient gender (Male/Female/Other/Unknown). Free-form
    /// string for forward-compat; the UI constrains it to a fixed set.</summary>
    public string Gender { get; set; } = "";
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

    /// <summary>
    /// Manual-override pin for <see cref="TemplateId"/> — set when the caller
    /// explicitly selected the template (create pin or PATCH override). While
    /// pinned, study-context changes never auto-rebind the template; clearing
    /// the pin (reset-to-auto) re-resolves it from the selection key.
    /// </summary>
    public bool TemplatePinned { get; set; }

    /// <summary>
    /// Manual-override pin for <see cref="RulebookId"/> — same semantics as
    /// <see cref="TemplatePinned"/>.
    /// </summary>
    public bool RulebookPinned { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Draft;

    /// <summary>F8 — RIS/worklist priority (default Routine). Settable via ingest or PATCH; drives
    /// the worklist queue ordering (STAT first) instead of a client-side heuristic.</summary>
    public ReportPriority Priority { get; set; } = ReportPriority.Routine;

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
    /// Iter-0a (PRD §14.12 / RPT-003) — flexible, template-bound structured
    /// data: numeric fields, table rows, and key/value common-data-elements
    /// that don't warrant their own column. The shape is governed by the bound
    /// <see cref="ReportTemplate.SectionsJson"/>. Queryable clinical data
    /// (RADS, measurements) lives in first-class child entities instead — see
    /// <see cref="RadsAssessments"/> and <see cref="Measurements"/>.
    /// </summary>
    public string StructuredFieldsJson { get; set; } = "{}";

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

    /// <summary>Iter-0a — first-class RADS assessments derived for / assigned to this report.</summary>
    public List<RadsAssessment> RadsAssessments { get; set; } = new();

    /// <summary>Iter-0a — first-class structured measurements (lesion-ready, cross-study linkable).</summary>
    public List<ReportMeasurement> Measurements { get; set; } = new();

    /// <summary>
    /// PR-N2 — soft-archive marker for the weekly orphaned-draft cleanup job
    /// (<c>OrphanedDraftCleanupJob</c>). Non-null means the draft was archived out of
    /// the active worklist after going stale for longer than the tenant's opt-in
    /// <see cref="Tenant.DraftAutoArchiveDays"/> window. The <see cref="Status"/> enum is
    /// deliberately NOT touched (no new state, no wire break); worklist queries simply add
    /// <c>ArchivedAt == null</c> and an <c>archived=true</c> recovery filter unarchives it.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; set; }
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

/// <summary>
/// Iter-0a (PRD §14.11/§14.12, RADS-001..008) — a single structured RADS
/// assessment attached to a report. Stored first-class (not in a JSON blob) so
/// the RADS engine, contradiction guard, and RADS analytics can query by
/// family/category across reports. One report may carry several assessments
/// (e.g. multiple breast lesions each with a BI-RADS category), so this is a
/// child collection rather than a single column.
/// </summary>
public class RadsAssessment : Entity
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    /// <summary>RADS family, e.g. "BI-RADS", "LI-RADS", "PI-RADS", "Lung-RADS", "TI-RADS", "O-RADS", "NI-RADS", "C-RADS".</summary>
    public string Family { get; set; } = "";
    /// <summary>Assigned category as authored, e.g. "4A", "3", "PI-RADS 5". Free text so each family's grammar is preserved.</summary>
    public string Category { get; set; } = "";
    /// <summary>Optional numeric score where the family is point-based (TI-RADS / O-RADS); null for categorical families.</summary>
    public int? Score { get; set; }
    /// <summary>True when the category was auto-derived by the RADS engine from structured inputs; false when a human assigned it.</summary>
    public bool IsDerived { get; set; }
    /// <summary>Optional anatomical scope / lesion this assessment refers to (e.g. "right breast UOQ"); links to <see cref="ReportMeasurement.LesionKey"/> when set.</summary>
    public string? LesionKey { get; set; }
    /// <summary>Human- or engine-supplied rationale for the category (feeds the "Why this category?" panel, RADS-003/012).</summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Iter-0a (PRD RPT-003, RADS-005, COMP-003/004) — a single structured
/// measurement attached to a report. First-class and queryable so longitudinal
/// lesion tracking and response-criteria engines (RECIST/iRECIST/Lugano) can
/// follow a lesion across studies via <see cref="LesionKey"/>. Generalises the
/// transient <see cref="RadioPad.Domain.ValueObjects.ExtractedMeasurement"/>
/// NLP value object into a persisted record.
/// </summary>
public class ReportMeasurement : Entity
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    /// <summary>Human label for the measured object, e.g. "Segment VII lesion".</summary>
    public string Label { get; set; } = "";
    /// <summary>Primary axis value.</summary>
    public double Value { get; set; }
    /// <summary>Unit of measure (mm, cm, HU, SUV, ADC, ...).</summary>
    public string Unit { get; set; } = "mm";
    /// <summary>Second axis for bi-/tri-axial measurements (null otherwise).</summary>
    public double? SecondValue { get; set; }
    /// <summary>Third axis for tri-axial measurements (null otherwise).</summary>
    public double? ThirdValue { get; set; }
    public string AnatomicalLocation { get; set; } = "";
    /// <summary>"left" | "right" | "bilateral" | "" (unspecified).</summary>
    public string Laterality { get; set; } = "";
    /// <summary>Report section the measurement was authored in, e.g. "Findings".</summary>
    public string Section { get; set; } = "";
    /// <summary>Stable per-lesion key used to correlate the same lesion across studies/reports (null for one-off measurements).</summary>
    public string? LesionKey { get; set; }
    /// <summary>Optional reference to the originating study/accession for cross-study tracking.</summary>
    public string? StudyReference { get; set; }
    /// <summary>"manual" (radiologist-entered) | "extracted" (NLP) | "imported".</summary>
    public string Source { get; set; } = "manual";
}

public enum SignatureRole
{
    Primary = 0,
    CoSigner = 1,
    Addendum = 2,
}

/// <summary>
/// PRD §14.15 (CR-001..010) — a PowerScribe-style critical-results communication
/// record attached to a report. Tracks the closed loop from "critical finding
/// logged" through "communicated to the ordering clinician" to
/// "acknowledged / closed", with a deadline derived from the
/// <see cref="Criticality"/>. First-class and queryable so the radiologist's
/// queue and the compliance list can scan by (tenant, status, due) without
/// parsing report bodies. NEVER auto-communicates and NEVER auto-signs — every
/// state change is an explicit human action recorded in the append-only audit log.
/// </summary>
public class CriticalResult : Entity
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    /// <summary>Criticality class; drives <see cref="DueAt"/> via <see cref="DeadlineFor"/>.</summary>
    public Criticality Criticality { get; set; } = Criticality.Red;
    /// <summary>Short human summary of the critical finding (e.g. "large pneumothorax, right").</summary>
    public string FindingSummary { get; set; } = "";
    public CriticalResultStatus Status { get; set; } = CriticalResultStatus.Open;
    /// <summary>Who the result was communicated to (ordering/referring clinician label). Null until communicated.</summary>
    public string? CommunicatedTo { get; set; }
    /// <summary>How it was communicated. Null until communicated.</summary>
    public CriticalCommunicationMethod? CommunicationMethod { get; set; }
    public DateTimeOffset? CommunicatedAt { get; set; }
    /// <summary>Free-text label of the person who acknowledged (read-back). Null until acknowledged.</summary>
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    /// <summary>Communication deadline computed from <see cref="Criticality"/> at creation time.</summary>
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Optional running notes appended by each state change.</summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// PRD §14.15 — communication deadline window per criticality class:
    /// Red = 15 min (immediate), Orange = 1 h (urgent), Yellow = 24 h (actionable).
    /// </summary>
    public static TimeSpan DeadlineFor(Criticality criticality) => criticality switch
    {
        Criticality.Red => TimeSpan.FromMinutes(15),
        Criticality.Orange => TimeSpan.FromHours(1),
        Criticality.Yellow => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(1),
    };
}

public class AiRequest : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ReportId { get; set; }
    public string PromptVersion { get; set; } = "";
    /// <summary>Iter-0b (RB-009 / AI-012) — rulebook entity id bound to the run (audit provenance), or null.</summary>
    public Guid? RulebookId { get; set; }
    /// <summary>Iter-0b (RB-009) — rulebook semantic version bound to the run (audit provenance).</summary>
    public string RulebookVersion { get; set; } = "";
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
/// PR-N2 — an outbound tenant webhook endpoint. Delivery is fanned out by
/// <c>WebhookDispatchJob</c>; every payload is PHI-minimized (ids/action/timestamps/
/// integrity hash only — never <c>DetailsJson</c> or clinical text) and signed with
/// <c>X-RadioPad-Signature: sha256=&lt;hex&gt;</c> HMAC over the raw body using
/// <see cref="Secret"/> (the same convention as <c>TenantSettings.FhirWebhookSecret</c>;
/// the column is AES-256-GCM encrypted at rest and is write-only over the API).
/// After 20 consecutive delivery failures the endpoint auto-disables (<see cref="DisabledAt"/>
/// set, <see cref="Active"/> false, audited <c>WebhookEndpointDisabled</c>).
/// </summary>
public class TenantWebhookEndpoint : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>Absolute HTTPS URL the PHI-minimized payload is POSTed to.</summary>
    public string Url { get; set; } = "";
    /// <summary>HMAC signing secret (encrypted at rest; never returned by the API).</summary>
    public string Secret { get; set; } = "";
    /// <summary>CSV of subscribed event kinds, e.g. <c>"audit"</c> or <c>"audit,notification"</c>.</summary>
    public string EventsCsv { get; set; } = "audit";
    public bool Active { get; set; } = true;
    /// <summary>Consecutive delivery failures; reset to 0 on the next 2xx.</summary>
    public int FailureCount { get; set; }
    /// <summary>Set when the endpoint auto-disabled after crossing the failure threshold.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}

/// <summary>
/// PR-N2 — a daily per-(tenant, provider, model) aggregate of <see cref="AiRequest"/>
/// counts and token sums, produced by <c>AiCostRollupJob</c>. Preserves billing counts
/// past the retention worker's purge of the raw <see cref="AiRequest"/> rows. Idempotent:
/// the (TenantId, Date, Provider, Model) tuple is unique and re-runs upsert in place.
/// </summary>
public class AiUsageRollup : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>The UTC calendar day the requests were made.</summary>
    public DateOnly Date { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int RequestCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}

/// <summary>
/// PR-N2 — a signed, PHI-minimized JSONL snapshot of one UTC day's audit chain for a
/// tenant, produced by <c>AuditExportRollupJob</c>. <see cref="ContentJsonl"/> is the same
/// PHI-minimized line shape as <c>AuditController.Siem</c> (ids/action/timestamps/integrity
/// hash — never <c>DetailsJson</c>) plus a trailing manifest line, optionally signed with
/// HMAC-SHA256 via the <c>AuditExport:SigningKey</c> config key. Idempotent by (TenantId,
/// Date); retained 90 days (pruned by <c>RetentionSweepJob</c>).
/// </summary>
public class AuditExportBundle : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>The exported UTC calendar day.</summary>
    public DateOnly Date { get; set; }
    /// <summary>PHI-minimized JSONL body + trailing manifest line.</summary>
    public string ContentJsonl { get; set; } = "";
    /// <summary>HMAC-SHA256 hex signature over <see cref="ContentJsonl"/>; null when no signing key is configured.</summary>
    public string? Signature { get; set; }
    public int EventCount { get; set; }
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
/// F7 — a per-user correction-dictionary entry, applied deterministically BEFORE the LLM (dictation
/// brief §6). Layered over the org <see cref="TenantLexicon"/>: for the same source term the user's
/// entry wins. "Fix a term once, applied to all future transcripts."
/// </summary>
public class UserCorrection : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>The spoken/mis-transcribed form to replace (e.g. "hypo dense").</summary>
    public string From { get; set; } = "";
    /// <summary>The canonical replacement (e.g. "hypodense").</summary>
    public string To { get; set; } = "";
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

    // Iter-0c (PRD §0.6 policy scaffold) — per-tenant security/clinical policy
    // fields. Persisted now so later iterations (Phase 2 access-control, Phase 3
    // critical results) enforce them without another migration. Defaults preserve
    // current behaviour (no enforcement) until the enforcing iteration lands.

    /// <summary>AUTH-004 — when true, every interactive sign-in for this tenant must complete MFA. Enforced in Phase 2 (Iter 2a/2f).</summary>
    public bool RequireMfa { get; set; } = false;
    /// <summary>AUTH-009 — idle session timeout in minutes; 0 = no idle timeout. Enforced in Phase 2 (Iter 2a).</summary>
    public int IdleTimeoutMinutes { get; set; } = 0;
    /// <summary>AUTH-009 — max concurrent active sessions per user; 0 = unlimited. Enforced in Phase 2 (Iter 2a).</summary>
    public int MaxConcurrentSessions { get; set; } = 0;
    /// <summary>§17.3 — JSON map of data class → retention days, e.g. {"audit":2555,"draft":90}. Empty {} = fall back to <see cref="RetentionDays"/>. Enforced in Phase 2 (Iter 2d).</summary>
    public string RetentionByClassJson { get; set; } = "{}";
    /// <summary>CR-002 — JSON array of criticality classes + SLAs, e.g. [{"name":"critical","ackMinutes":30}]. Empty [] = built-in defaults. Consumed in Phase 3 (Iter 3a).</summary>
    public string CriticalityClassesJson { get; set; } = "[]";

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

/// <summary>
/// Desktop↔phone companion pairing session. A radiologist's desktop app (report
/// open) advertises a session with a short <see cref="PairingCode"/>; their phone
/// joins by that code and streams voice dictation + remote commands to the desktop
/// through the cloud <c>/ws/companion</c> relay (the phone cannot reach the
/// desktop's loopback sidecar directly). The row is the durable coordination
/// record; the live dictation stream is relayed in-memory and never persisted.
/// </summary>
public class CompanionSession : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>The radiologist who opened the session on the desktop. The phone
    /// must authenticate as the SAME tenant + user to pair.</summary>
    public Guid UserId { get; set; }
    /// <summary>Short human-typeable pairing code (6 uppercase alphanumerics),
    /// unique among active sessions.</summary>
    public string PairingCode { get; set; } = "";
    /// <summary>Advertised name of the desktop that created the session.</summary>
    public string HostDeviceName { get; set; } = "";
    /// <summary>Name of the phone that paired; null until <see cref="Status"/> becomes
    /// <see cref="CompanionSessionStatus.Paired"/>.</summary>
    public string? CompanionDeviceName { get; set; }
    public CompanionSessionStatus Status { get; set; } = CompanionSessionStatus.Advertising;
    /// <summary>The code stops being pairable once this passes (default: 5 minutes).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>When the phone paired; null while still advertising / ended / expired.</summary>
    public DateTimeOffset? PairedAt { get; set; }
}


/// <summary>
/// Durable counterpart to <c>RadioPad.Api.Services.AiJobRegistry</c>'s in-memory
/// hot cache — the row of record for one AI report-generation attempt
/// (impression/rewrite or whole-report generate), across the local/on-device,
/// UBAG, and hosted-provider paths. The registry serves the ~2s poll cadence
/// from memory; this table is what survives a restart: <c>AiJobCoordinator</c>
/// (RadioPad.Api/Services) writes here on every state transition, and the
/// boot-time recovery sweep marks orphaned queued/running rows as failed
/// (errorKind "server_restart") rather than silently forgetting them — a job
/// is never auto-re-run without a radiologist present to supervise it.
/// </summary>
public class AiJob : Entity
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>"ai" (impression/rewrite) | "generate" (whole-report).</summary>
    public string Kind { get; set; } = "";

    /// <summary>The AI mode when Kind == "ai" (impression, rewrite, ...); "generate" when Kind == "generate".</summary>
    public string Mode { get; set; } = "";

    public Guid? ProviderId { get; set; }

    /// <summary>queued | running | ok | error | cancelled — mirrors AiJobRegistry.AiJobState.Status
    /// plus the additive "queued"/"cancelled" values the durable layer adds.</summary>
    public string Status { get; set; } = "queued";

    /// <summary>Endpoint-shaped JSON payload for Kind == "ai" once Status == "ok"; null for
    /// Kind == "generate" (the report row + its ReportVersion snapshot ARE the result — never
    /// duplicate clinical text into a second store). Cleared 24h after completion by RetentionWorker.</summary>
    public string? ResultJson { get; set; }

    /// <summary>PR-B5 — request payload for the input-carrying job kinds: the raw dictation for an
    /// <c>ai</c>+<c>cleanup</c> job (<c>{ rawDictation }</c>), or <c>{ text, sectionKey, useUbag }</c>
    /// for a <c>crosscheck</c> job. Needed so <c>POST /api/jobs/{id}/retry</c> can re-run the job.
    /// Same clinical-text-at-rest class as <see cref="ResultJson"/> — nulled 24h after completion by
    /// the retention sweep, after which the job is no longer retryable — and NEVER serialized into any
    /// API response (the client already holds the raw input it submitted).</summary>
    public string? InputJson { get; set; }

    public string? Error { get; set; }

    /// <summary>Stable machine-readable failure reason: not_found, report_modified, quota_exceeded,
    /// provider_policy, provider_transport, rulebook_governance, timeout, server_error, or the
    /// durable-layer addition server_restart (boot recovery sweep).</summary>
    public string? ErrorKind { get; set; }

    /// <summary>UBAG gateway job id, when the provider path is UBAG. Invariant (PR-B4):
    /// ONLY the UBAG adapter populates this (via <c>AiCompletionRequest.OnProviderJobCreated</c>);
    /// boot recovery treats its presence on a still-<c>running</c> row as "a UBAG gateway job may
    /// still be running", and re-attaches + re-polls that gateway job instead of sweeping the row
    /// to <c>server_restart</c>. Empty for every non-UBAG provider path.</summary>
    public string? ProviderJobId { get; set; }

    public int Attempt { get; set; } = 1;

    /// <summary>Set when this row was created by POST /api/jobs/{id}/retry — links to the job
    /// it retried. Retrying always creates a new row; it never resurrects the old one.</summary>
    public Guid? RetryOfJobId { get; set; }

    /// <summary>Set by POST /api/jobs/{id}/cancel while Status is queued/running; observed by
    /// the runner's linked cancellation token on its next check.</summary>
    public bool CancelRequested { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// NOTIF-001 — one in-app notification row destined for a single recipient
/// (<see cref="UserId"/>). Persisted per-recipient (fan-out at creation time) so
/// the inbox, unread-count, and read/ack state are all a simple own-rows query.
///
/// PHI posture (NOTIF-004): <see cref="Title"/> / <see cref="Body"/> are the
/// authenticated in-app tier — they may carry a modality/body-part-class
/// descriptor but NEVER raw clinical narrative, findings, or an accession. The
/// audit trail (<see cref="Enums.AuditAction.NotificationCreated"/>) records
/// workflow metadata only and never echoes Title/Body.
/// </summary>
public class Notification : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>The recipient. Every notification is single-recipient.</summary>
    public Guid UserId { get; set; }
    public NotificationCategory Category { get; set; }
    public NotificationUrgency Urgency { get; set; }
    /// <summary>In-app-safe headline; never raw clinical narrative.</summary>
    public string Title { get; set; } = "";
    /// <summary>In-app tier of NOTIF-004 (may carry modality/body-part class); never findings/accession.</summary>
    public string Body { get; set; } = "";
    /// <summary>App-relative deep link, e.g. <c>/reports/view?id=…&amp;aiJob=…</c>.</summary>
    public string? LinkHref { get; set; }
    /// <summary>"aiJob" | "criticalResult" | "peerReview" | "rulebook" | "template" | "system".</summary>
    public string? SourceKind { get; set; }
    public Guid? SourceId { get; set; }
    public bool RequiresAck { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    /// <summary>Always == <see cref="UserId"/> today; the column anticipates future delegated ack.</summary>
    public Guid? AcknowledgedByUserId { get; set; }
    /// <summary>Idempotency key — a unique filtered index collapses duplicate producer events.</summary>
    public string? DedupeKey { get; set; }
}

/// <summary>
/// NOTIF-001 — one row per (tenant, user) capturing a recipient's notification
/// preferences: muted categories (NOTIF-009 — critical classes may never be
/// muted), a Do-Not-Disturb window (NOTIF-007 — suppresses push/OS-toast/email
/// dispatch only, never the inbox row + SSE), and per-channel opt-ins.
/// </summary>
public class NotificationPreference : Entity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>CSV of muted <see cref="NotificationCategory"/> names; the prefs PUT rejects
    /// muting any category in the tenant's critical-class list (NOTIF-009).</summary>
    public string MutedCategoriesCsv { get; set; } = "";
    /// <summary>Local-time DND window start, minutes-of-day [0..1440). Null disables DND.</summary>
    public int? DndStartMinutes { get; set; }
    /// <summary>Local-time DND window end, minutes-of-day (0..1440]. Null disables DND.</summary>
    public int? DndEndMinutes { get; set; }
    /// <summary>IANA/Windows time-zone id the DND window is evaluated in; empty means UTC.</summary>
    public string DndTimeZone { get; set; } = "";
    /// <summary>Push dispatch opt-in (Critical urgency only regardless — PR-N4).</summary>
    public bool PushEnabled { get; set; } = true;
    /// <summary>Email dispatch opt-in (Critical urgency only regardless — PR-N4).</summary>
    public bool EmailEnabled { get; set; } = false;
}

/// <summary>
/// PRD §14.13 (PR-001..010) — one RADPEER-aligned peer-review assignment of a
/// signed report to a second radiologist. Never a clinical decision system: the
/// score is a quality benchmark, it does not change the report and it never
/// signs anything.
///
/// PR-002 (anonymized assignment) is modelled by <see cref="Blinded"/>: while the
/// review is open the API withholds <see cref="OriginalAuthorUserId"/> from the
/// reviewer's projection, so a reviewer scores the report, not the colleague.
/// The column itself is always populated — the analytics in PR-009 need it, and
/// hiding it in the DB would make per-reader concordance impossible to compute.
/// </summary>
public class PeerReview : Entity
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    /// <summary>The second radiologist who scores the case. Never equal to <see cref="OriginalAuthorUserId"/>.</summary>
    public Guid ReviewerUserId { get; set; }
    /// <summary>The radiologist whose interpretation is under review (the report's author).</summary>
    public Guid OriginalAuthorUserId { get; set; }
    /// <summary>Who created the assignment (director/quality admin, or the sampling sweep's operator).</summary>
    public Guid AssignedByUserId { get; set; }

    public PeerReviewType ReviewType { get; set; } = PeerReviewType.Random;
    public PeerReviewStatus Status { get; set; } = PeerReviewStatus.Assigned;

    /// <summary>RADPEER 1..4; <see cref="PeerReviewScore.NotScored"/> until submitted.</summary>
    public PeerReviewScore Score { get; set; } = PeerReviewScore.NotScored;
    /// <summary>RADPEER difficulty modifier recorded with the score.</summary>
    public PeerReviewComplexity Complexity { get; set; } = PeerReviewComplexity.Routine;
    public PeerReviewDiscrepancyCategory DiscrepancyCategory { get; set; } = PeerReviewDiscrepancyCategory.None;

    /// <summary>PR-003 — the reviewer's structured rationale / free-text comments.</summary>
    public string Comments { get; set; } = "";

    /// <summary>PR-002 — whether the reviewer is blinded to the author until they submit.</summary>
    public bool Blinded { get; set; } = true;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Author's rebuttal when they contest the score; null unless <see cref="Status"/> is Disputed.</summary>
    public string? DisputeReason { get; set; }
    public DateTimeOffset? DisputedAt { get; set; }

    /// <summary>True once the reviewer has submitted — the point at which blinding lifts.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsUnblinded => !Blinded || Status is PeerReviewStatus.Completed or PeerReviewStatus.Disputed;

    /// <summary>A score of 2..4 is a recorded discrepancy; 1 (concur) and NotScored are not.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsDiscrepancy => Score is PeerReviewScore.DiscrepancyUnlikelySignificant
        or PeerReviewScore.DiscrepancyShouldBeMadeMostOfTheTime
        or PeerReviewScore.DiscrepancyShouldBeMadeAlmostEveryTime;
}

/// <summary>
/// PRD §14.14 (TF-001..008) — one de-identified teaching-file case.
///
/// SAFETY: this entity is a PHI-free surface by construction. It deliberately
/// has NO accession number, NO patient reference/MRN, NO name, and NO date of
/// birth column — there is nowhere to put them even by accident. Every text
/// field that can originate from a report is passed through
/// <c>TeachingCaseDeidentifier</c> before it is assigned (TF-002).
/// <see cref="SourceReportId"/> is a tenant-internal provenance pointer only:
/// it never travels with an export and resolving it still requires
/// report-read permission in the same tenant.
/// </summary>
public class TeachingCase : Entity
{
    public Guid TenantId { get; set; }
    /// <summary>Author — the only non-admin who may edit, publish, or delete the case.</summary>
    public Guid CreatedByUserId { get; set; }

    public string Title { get; set; } = "";
    public string Modality { get; set; } = "";
    public string BodyPart { get; set; } = "";

    /// <summary>TF-004 — the teaching diagnosis ("acute appendicitis", "LI-RADS 5 HCC").</summary>
    public string Diagnosis { get; set; } = "";

    /// <summary>TF-004 — the teaching pearls: why this case is worth studying.</summary>
    public string TeachingPoints { get; set; } = "";

    /// <summary>De-identified clinical history / indication (TF-002).</summary>
    public string ClinicalHistory { get; set; } = "";

    /// <summary>De-identified findings narrative (TF-002).</summary>
    public string FindingsText { get; set; } = "";

    /// <summary>De-identified impression narrative (TF-002).</summary>
    public string ImpressionText { get; set; } = "";

    /// <summary>
    /// TF-004 — comma-separated free tags ("RADS", subspecialty, pathology).
    /// Matches the CSV convention already used by
    /// <see cref="Rulebook.AppliesToModalities"/>; a child entity would buy
    /// nothing at this cardinality.
    /// </summary>
    public string Tags { get; set; } = "";

    public TeachingDifficulty Difficulty { get; set; } = TeachingDifficulty.Intermediate;

    /// <summary>
    /// Provenance of the report this case was de-identified from, or null for
    /// a hand-authored case. Never exported and never surfaced to a reader who
    /// is not the author.
    /// </summary>
    public Guid? SourceReportId { get; set; }

    public TeachingVisibility Visibility { get; set; } = TeachingVisibility.Private;

    /// <summary>Set when the case is first published to the tenant library; cleared on unpublish.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>TF-008 — read counter, incremented on each detail fetch by a non-author.</summary>
    public int ViewCount { get; set; }
}
