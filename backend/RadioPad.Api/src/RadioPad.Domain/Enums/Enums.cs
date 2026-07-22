namespace RadioPad.Domain.Enums;

/// <summary>
/// Compliance class assigned to an AI provider configuration. Used by the
/// AI gateway to decide whether a given request (which may carry PHI) is
/// allowed to leave the tenant boundary toward that provider.
/// </summary>
public enum ProviderComplianceClass
{
    /// <summary>Provider is blocked. No requests routed.</summary>
    Blocked = 0,

    /// <summary>Sandbox/demo only. Never accepts PHI.</summary>
    Sandbox = 1,

    /// <summary>De-identified payloads only. PHI must be stripped first.</summary>
    DeIdentifiedOnly = 2,

    /// <summary>Approved for PHI under a signed BAA / DPA / equivalent.</summary>
    PhiApproved = 3,

    /// <summary>Local/on-prem model — never leaves the device or VPC.</summary>
    LocalOnly = 4,
}

public enum RulebookStatus
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Deprecated = 3,
}

public enum ReportStatus
{
    Draft = 0,
    Validated = 1,
    Acknowledged = 2,
    Exported = 3,
}

/// <summary>F8 — RIS-driven report priority that drives worklist queue ordering (STAT first).</summary>
public enum ReportPriority
{
    Routine = 0,
    Urgent = 1,
    Stat = 2,
}

/// <summary>
/// PRD §14.15 (CR-001..010) — clinical criticality class of a critical result,
/// PowerScribe-style. Drives the communication deadline (see
/// <see cref="Entities.CriticalResult.DeadlineFor"/>): Red is immediate /
/// life-threatening, Orange is urgent, Yellow is actionable-but-routine.
/// </summary>
public enum Criticality
{
    /// <summary>Immediate / life-threatening — must be communicated at once.</summary>
    Red = 0,
    /// <summary>Urgent — communicate within the hour.</summary>
    Orange = 1,
    /// <summary>Actionable — communicate within the working day.</summary>
    Yellow = 2,
}

/// <summary>
/// PRD §14.15 — lifecycle of a <see cref="Entities.CriticalResult"/>. New rows
/// land <see cref="Open"/>; recording the call/message moves them to
/// <see cref="Communicated"/>; the receiver read-back acknowledgement moves them
/// to <see cref="Acknowledged"/>. <see cref="Escalated"/> is set (manually or by
/// the overdue background sweep) when an Open result blows its deadline;
/// <see cref="Closed"/> is terminal.
/// </summary>
public enum CriticalResultStatus
{
    Open = 0,
    Communicated = 1,
    Acknowledged = 2,
    Escalated = 3,
    Closed = 4,
}

/// <summary>PRD §14.15 — how the critical result was communicated to the ordering/referring clinician.</summary>
public enum CriticalCommunicationMethod
{
    Phone = 0,
    SecureMessage = 1,
    InPerson = 2,
    Other = 3,
}

/// <summary>
/// Lifecycle of a <see cref="Entities.CompanionSession"/> (desktop↔phone pairing).
/// A session is created <see cref="Advertising"/>; the phone joining by code moves
/// it to <see cref="Paired"/>. <see cref="Ended"/> (explicit teardown) and
/// <see cref="Expired"/> (code TTL elapsed before pairing) are terminal.
/// </summary>
public enum CompanionSessionStatus
{
    Advertising = 0,
    Paired = 1,
    Ended = 2,
    Expired = 3,
}

public enum UserRole
{
    Radiologist = 0,
    ReportingAdmin = 1,
    MedicalDirector = 2,
    ComplianceReviewer = 3,
    ItAdmin = 4,
    BillingAdmin = 5,
    // Iter-0c (PRD AUTH-002) — the remaining canonical RBAC roles.
    /// <summary>Trainee radiologist: draft/edit/validate but never signs or exports a final report.</summary>
    Resident = 6,
    /// <summary>Senior trainee: as Resident, plus may export; final sign still reserved for attending roles.</summary>
    Fellow = 7,
    /// <summary>Attending subspecialty radiologist: full reporting authority (same as Radiologist).</summary>
    Subspecialist = 8,
    /// <summary>Research user: read-only access to de-identified reporting artifacts.</summary>
    Researcher = 9,
    /// <summary>Read-only auditor: read + audit verify/export across the tenant; no mutations.</summary>
    Auditor = 10,
}

public enum RbacPermission
{
    ReportsRead = 0,
    ReportsDraft = 1,
    ReportsEdit = 2,
    ReportsValidate = 3,
    ReportsSign = 4,
    ReportsExport = 5,
    RulebooksRead = 20,
    RulebooksManage = 21,
    RulebooksApprove = 22,
    TemplatesRead = 40,
    TemplatesManage = 41,
    TemplatesApprove = 42,
    ProvidersRead = 60,
    ProvidersManage = 61,
    AuditRead = 80,
    AuditVerify = 81,
    AuditExport = 82,
    UsersRead = 100,
    UsersManage = 101,
    UsersRevokeSessions = 102,
    BillingRead = 120,
    BillingManage = 121,
    SecurityManage = 140,
    TenantSettingsManage = 141,
    ValidationPacksRead = 160,
    ValidationPacksManage = 161,
    ValidationPacksRun = 162,
    McpToolsInvoke = 180,
    McpToolsManage = 181,
    PromptOverridesManage = 200,
    PromptOverridesApprove = 201,
    // Iter-36 — admin-managed Modality + BodyPart catalogs.
    ModalitiesRead = 220,
    ModalitiesManage = 221,
    BodyPartsRead = 240,
    BodyPartsManage = 241,
    // PRD §14.15 (CR-001..010) — critical-results communication tracking.
    CriticalResultsRead = 260,
    CriticalResultsManage = 261,
    // PRD §14.13 (PR-001..010) — RADPEER-aligned peer review & quality.
    PeerReviewRead = 300,
    PeerReviewSubmit = 301,
    PeerReviewManage = 302,
    // PRD §14.14 (TF-001..008) — teaching file & education module.
    TeachingCasesRead = 320,
    TeachingCasesManage = 321,
}

public enum AuditAction
{
    AiRequest = 0,
    AiResponse = 1,
    ReportEdited = 2,
    ReportExported = 3,
    ReportAcknowledged = 4,
    ProviderBlocked = 5,
    RulebookApproved = 6,
    RulebookDeprecated = 7,
    UserLogin = 8,
    PolicyViolation = 9,
    /// <summary>PRD INT-001..004 — inbound HL7/FHIR ingest webhook delivered an order.</summary>
    OrderIngested = 10,
    /// <summary>PRD DCM-001..006 — DICOMweb study-context fetch.</summary>
    DicomContextFetched = 11,
    /// <summary>PRD §13.3 — retention worker purged stale rows for this tenant.</summary>
    RetentionPurge = 12,
    /// <summary>PRD BILL-001..006 — billing/subscription state changed (Stripe webhook, manual override, suspension, etc.).</summary>
    BillingChanged = 13,
    /// <summary>Iter-30 — bidirectional FHIR ingest: a DiagnosticReport was imported as a Draft.</summary>
    ReportImported = 14,
    /// <summary>Iter-30 — multi-radiologist sign-off: a Primary / CoSigner / Addendum signature was attached to a report.</summary>
    ReportSigned = 15,
    /// <summary>Iter-30 — addendum body appended to an already-signed report (creates a new ReportVersion).</summary>
    ReportAddendumAppended = 16,
    /// <summary>PRD MOB-007 — mobile push device registered for the (tenant, user).</summary>
    PushDeviceRegistered = 17,
    /// <summary>PRD MOB-007 — mobile push device unregistered.</summary>
    PushDeviceUnregistered = 18,
    /// <summary>PRD MOB-007 — admin-issued test push notification was dispatched (or attempted) to a registered device.</summary>
    PushDeviceTested = 19,
    /// <summary>Iter-31 TMP-005 — a report template was approved for production use.</summary>
    TemplateApproved = 20,
    /// <summary>Iter-31 AUTH-006 — a user account was locked out (IsActive flipped to false by an admin).</summary>
    UserLockedOut = 21,
    /// <summary>Iter-31 AUTH-006 — a previously locked-out user was reinstated (IsActive flipped back to true).</summary>
    UserUnlocked = 22,
    /// <summary>Iter-31 STD-005/STD-006 — tenant lexicon was bulk-imported from YAML/JSON.</summary>
    LexiconImported = 23,
    /// <summary>Iter-31 STD-005/STD-006 — tenant lexicon was bulk-exported as YAML/JSON.</summary>
    LexiconExported = 24,
    /// <summary>Iter-31 SEC-011 — anomaly detector raised an alert (provider-block burst, policy-violation burst, audit-chain breakage).</summary>
    AnomalyDetected = 25,
    /// <summary>Iter-31 MCP-002 — admin approved an MCP tool registration.</summary>
    McpToolApproved = 26,
    /// <summary>Iter-31 MCP-002 — admin revoked an MCP tool registration.</summary>
    McpToolRevoked = 27,
    /// <summary>Iter-31 MCP-004 — an MCP tool was invoked (input/output stored as SHA-256 hashes only).</summary>
    McpToolCalled = 28,
    /// <summary>Iter-32 AUTH-005 — SCIM bearer token was rotated by an admin (only the new bearer is returned, exactly once).</summary>
    ScimBearerRotated = 29,
    /// <summary>Iter-32 AUTH-005 — SCIM Group resource was created, updated, or deleted by the IdP.</summary>
    ScimGroupChanged = 30,
    /// <summary>Iter-32 MCP-001 — a new MCP tool manifest was submitted to the registry.</summary>
    McpToolRegistered = 31,
    /// <summary>Iter-32 MCP-002 — admin blocked an MCP tool (scope violation, signature failure, manual revoke).</summary>
    McpToolBlocked = 32,
    /// <summary>Iter-32 AUTH-006 / SEC-008 — all active sessions / bearer tokens for a user were revoked (admin or self-service).</summary>
    SessionsRevoked = 33, // iter-32
    /// <summary>Iter-32 SEC-011 — anomaly detector raised a high-severity security alert (provider-block / policy-violation / login-failure burst, or AI-spike vs 24 h baseline).</summary>
    SecurityAlert = 34, // iter-32
    /// <summary>Iter-32 AI-009 — a tenant prompt-block override was approved by a Medical Director.</summary>
    PromptOverrideApproved = 35,
    /// <summary>Iter-32 TMP-005 — a report template was deprecated (any → Deprecated).</summary>
    TemplateDeprecated = 36,
    /// <summary>Iter-32 TMP-005 — a draft report template was submitted for review (Draft → Review).</summary>
    TemplateSubmittedForReview = 37,
    /// <summary>Iter-33 INT-008 — Orthanc bridge reported a stable study landing in PACS (study summary received over the bearer-protected /api/integrations/orthanc/study-stable hook).</summary>
    StudyReceived = 38,
    /// <summary>Iter-33 AUTH-004 — a request was rejected by an application-level rate limiter (e.g. magic-link per-email / per-IP). Audit row records the rejection scope without leaking the email.</summary>
    RateLimited = 39,
    /// <summary>Iter-33 PERF-004 — Alertmanager (or compatible) webhook posted an SLO burn-rate alert.</summary>
    SystemAlert = 40,
    /// <summary>
    /// Iter-35 PROV-007 — an OAuth refresh token was saved, rotated, or
    /// deleted in the per-tenant refresh-token vault. Audit details record
    /// the action kind (<c>saved</c> / <c>rotated</c> / <c>deleted</c>) and
    /// the provider id but never the token bytes or ciphertext.
    /// </summary>
    OAuthRefreshRotated = 41,
    /// <summary>Iter-35 — clinical validation pack (rulebook golden suite) was approved by a Medical Director.</summary>
    ValidationPackApproved = 42,
    /// <summary>Iter-35 — clinical validation pack was deprecated.</summary>
    ValidationPackDeprecated = 43,
    /// <summary>Iter-35 — clinical validation pack was executed against its rulebook (records pass/fail counts).</summary>
    ValidationPackRun = 44,
    /// <summary>PRD Enterprise GA #13 — a rulebook/template was submitted to the marketplace for review.</summary>
    MarketplaceSubmission = 45,
    /// <summary>PRD Enterprise GA #13 — a marketplace submission was approved by an admin/MedicalDirector.</summary>
    MarketplaceApproved = 46,
    /// <summary>PRD Enterprise GA #13 — a marketplace submission was rejected by an admin/MedicalDirector.</summary>
    MarketplaceRejected = 47,
    /// <summary>PRD Enterprise GA #13 — a marketplace listing was installed into a tenant.</summary>
    MarketplaceInstalled = 48,
    /// <summary>An AI provider configuration was created or updated by an admin
    /// (POST /api/providers). A routine admin action — NOT a policy violation
    /// (it was previously mis-tagged as <see cref="PolicyViolation"/>).</summary>
    ProviderConfigChanged = 54,
    /// <summary>Phase B (dictation transcription) — a dictation audio file was
    /// transcribed via the UBAG <c>medical_transcription</c> flow. Details
    /// record provider/target/artifact metadata and the SHA-256 of the
    /// transcript only — never the transcript text or audio bytes.</summary>
    AudioTranscribed = 55,
    /// <summary>Self-serve SaaS onboarding — a new tenant (organization) and its
    /// first admin user were created via <c>POST /api/registration/create-organization</c>.
    /// Details record the slug and admin email; no secrets are stored.</summary>
    OrganizationCreated = 56,
    /// <summary>Master-admin user management — a tenant user was created by an
    /// administrator (<c>POST /api/users</c>). Details record the target email
    /// and role; the temporary password is never stored in the audit row.</summary>
    UserCreated = 57,
    /// <summary>Master-admin user management — a tenant user's profile/role/active
    /// state was updated by an administrator (<c>PATCH /api/users/{id}</c>).</summary>
    UserUpdated = 58,
    /// <summary>Master-admin user management — a tenant user was soft-deleted
    /// (deprovisioned) or hard-deleted by an administrator (<c>DELETE /api/users/{id}</c>).</summary>
    UserDeleted = 59,
    /// <summary>Credential change — a user's password was set or reset (self-service
    /// change, TOTP-based reset, or admin reset). The password is never stored.</summary>
    PasswordChanged = 60,
    /// <summary>Master-admin user management — a user's enrolled TOTP authenticator
    /// was cleared so they must re-enroll on next sign-in (<c>POST /api/users/{id}/reset-mfa</c>).</summary>
    UserMfaReset = 61,
    /// <summary>Companion relay — a phone paired to a desktop's advertised companion
    /// session (<c>POST /api/companion/pair</c>). Details record the session id only;
    /// never any dictation text or report content (transient relay, no PHI).</summary>
    CompanionPaired = 62,
    /// <summary>PRD §14.15 (CR-001) — a critical result was logged against a report.
    /// Details record the critical-result id, report id, criticality, and due time;
    /// never the finding narrative (which may carry clinical text).</summary>
    CriticalResultCreated = 63,
    /// <summary>PRD §14.15 (CR-004) — the ordering/referring clinician was notified of a
    /// critical result. Details record the id, method, and recipient label — no PHI narrative.</summary>
    CriticalResultCommunicated = 64,
    /// <summary>PRD §14.15 (CR-005) — a critical result communication was acknowledged (read-back).</summary>
    CriticalResultAcknowledged = 65,
    /// <summary>PRD §14.15 (CR-007) — a critical result was escalated (manually, or by the overdue sweep when its deadline lapsed while Open).</summary>
    CriticalResultEscalated = 66,
    /// <summary>PRD §14.15 (CR-008) — a critical result was closed out (terminal).</summary>
    CriticalResultClosed = 67,
    /// <summary>PRD §14.13 (PR-001/PR-002) — a signed report was assigned to a peer reviewer
    /// (single assignment or a random-sampling batch). Details record the review id, report id,
    /// review type, and whether the assignment is blinded — never report narrative.</summary>
    PeerReviewAssigned = 80,
    /// <summary>PRD §14.13 (PR-003) — a reviewer submitted a RADPEER-style score. Details record
    /// the review id, score, complexity, and discrepancy category — never the free-text rationale.</summary>
    PeerReviewSubmitted = 81,
    /// <summary>PRD §14.13 — the original author disputed a completed peer review.</summary>
    PeerReviewDisputed = 82,
    /// <summary>PRD §14.14 (TF-001/TF-002) — a teaching case was created (blank, or de-identified
    /// from a source report). Details record the teaching-case id, the source report id, and
    /// whether de-identification ran — never the case narrative or any patient identifier.</summary>
    TeachingCaseCreated = 100,
    /// <summary>PRD §14.14 (TF-007) — a teaching case was published to the tenant library
    /// (Private → Tenant visibility).</summary>
    TeachingCasePublished = 101,
    /// <summary>PRD §14.14 (TF-007) — a teaching case was withdrawn from the tenant library
    /// (Tenant → Private visibility).</summary>
    TeachingCaseUnpublished = 102,
    /// <summary>PRD §14.14 — a teaching case was deleted by its owner or an administrator.</summary>
    TeachingCaseDeleted = 103,
    /// <summary>PRD RPT-021 — a shared (tenant / subspecialty) autotext macro was created,
    /// updated, or deleted. Details record the macro id, trigger, scope and which action ran;
    /// the expansion body is authored boilerplate but is not echoed into the audit trail.</summary>
    MacroChanged = 120,
    /// <summary>A radiologist cancelled a running or queued async AI job via
    /// <c>POST /api/jobs/{id}/cancel</c>. Details record the job id and the status it held
    /// when cancellation was requested; no clinical text is echoed into the audit trail.</summary>
    AiJobCancelled = 121,
    /// <summary>A radiologist retried a failed or cancelled async AI job via
    /// <c>POST /api/jobs/{id}/retry</c>. Details link the new job id to the one it retried
    /// (<c>retryOfJobId</c>); no clinical text is echoed into the audit trail.</summary>
    AiJobRetried = 122,
}

/// <summary>
/// Iter-35 — lifecycle of a <see cref="Entities.ValidationPack"/>. New rows
/// land in <see cref="Draft"/>; promotion to <see cref="Approved"/> requires
/// a Medical Director (or ItAdmin). <see cref="Deprecated"/> is terminal;
/// once deprecated a pack cannot be re-approved (callers must create a new
/// pack to re-certify a rulebook).
/// </summary>
public enum ValidationPackStatus
{
    Draft = 0,
    Approved = 1,
    Deprecated = 2,
}

/// <summary>
/// Iter-32 AI-009 — lifecycle of a <see cref="Entities.PromptOverride"/>.
/// New rows are created in <c>Draft</c> and only take effect after a
/// Medical Director marks them <c>Approved</c>; <c>EfPromptOverrideStore</c>
/// filters loads to <c>Approved</c> only.
/// </summary>
public enum PromptOverrideStatus
{
    Draft = 0,
    Approved = 1,
}

/// <summary>
/// PRD Enterprise GA #13 — lifecycle of a marketplace submission.
/// Draft → PendingReview → Approved / Rejected. Deprecated is terminal.
/// </summary>
public enum MarketplaceSubmissionStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Rejected = 3,
    Deprecated = 4,
}

public enum ValidationSeverity
{
    Info = 0,
    Warning = 1,
    Blocker = 2,
}

/// <summary>PRD BILL-001/006. Plan tier governs feature flags + Stripe price ids.</summary>
public enum TenantPlan
{
    Trial = 0,
    Team = 1,
    Enterprise = 2,
}

/// <summary>
/// Iter-31 TMP-005 — lifecycle of a <see cref="Entities.ReportTemplate"/>.
/// Mirrors <see cref="RulebookStatus"/> so the UI can reuse the same chips.
/// </summary>
public enum TemplateStatus
{
    Draft = 0,
    Approved = 1,
    Deprecated = 2,
    /// <summary>Iter-32 TMP-005 — submitted for review by an admin; pending approval.</summary>
    Review = 3,
}

/// <summary>
/// Iter-31 TMP-003 — variant of a report template. The same template id may
/// ship multiple variants (Normal/Abnormal/...) so radiologists can pick the
/// closest starting point per case.
/// </summary>
public enum TemplateVariant
{
    Normal = 0,
    Abnormal = 1,
    FollowUp = 2,
    Screening = 3,
    Urgent = 4,
}

/// <summary>
/// PRD §14.13 (PR-001) — how a case entered the peer-review queue.
/// <see cref="Random"/> is the routine RADPEER sampling stream; the rest are
/// rule-based / workflow-driven selections.
/// </summary>
public enum PeerReviewType
{
    /// <summary>PR-001 — picked by the random sampling sweep over signed reports.</summary>
    Random = 0,
    /// <summary>PR-001/PR-006 — deliberately selected (STAT, high-risk finding, complaint follow-up).</summary>
    Targeted = 1,
    /// <summary>PR-006 — second read sought before the case is closed; both reads stand together.</summary>
    Consensus = 2,
    /// <summary>Review triggered by an addendum to an already-signed report.</summary>
    Addendum = 3,
}

/// <summary>
/// PRD §14.13 (PR-003) — RADPEER-style 4-point agreement score. 1 = concur;
/// 2..4 grade the discrepancy by how reliably the finding should have been made.
/// <see cref="NotScored"/> is the pre-submission sentinel so an Assigned row is
/// never mistaken for a recorded "concur".
/// </summary>
public enum PeerReviewScore
{
    /// <summary>Not yet scored — the review is still Assigned/InProgress.</summary>
    NotScored = 0,
    /// <summary>RADPEER 1 — concur with the original interpretation.</summary>
    Concur = 1,
    /// <summary>RADPEER 2 — discrepancy, unlikely to be clinically significant.</summary>
    DiscrepancyUnlikelySignificant = 2,
    /// <summary>RADPEER 3 — discrepancy that should be made most of the time.</summary>
    DiscrepancyShouldBeMadeMostOfTheTime = 3,
    /// <summary>RADPEER 4 — discrepancy that should be made almost every time.</summary>
    DiscrepancyShouldBeMadeAlmostEveryTime = 4,
}

/// <summary>
/// PRD §14.13 (PR-003) — the RADPEER difficulty modifier ("a"/"b"). Recorded
/// alongside the score so a discrepancy on a genuinely hard study is not
/// benchmarked against a routine one.
/// </summary>
public enum PeerReviewComplexity
{
    /// <summary>RADPEER "a" — routine study / unremarkable difficulty.</summary>
    Routine = 0,
    /// <summary>RADPEER "b" — difficult study (complex anatomy, limited technique, subtle finding).</summary>
    Complex = 1,
}

/// <summary>PRD §14.13 (PR-003/PR-009) — structured rationale: where the discrepancy came from.</summary>
public enum PeerReviewDiscrepancyCategory
{
    /// <summary>No discrepancy (pairs with <see cref="PeerReviewScore.Concur"/>).</summary>
    None = 0,
    /// <summary>The finding was present on the images but not seen.</summary>
    Perceptual = 1,
    /// <summary>The finding was seen but characterised or graded differently.</summary>
    Interpretive = 2,
    /// <summary>Finding correct, but the report/communication failed to convey it.</summary>
    Communication = 3,
    /// <summary>Acquisition/protocol limitation drove the difference.</summary>
    Technique = 4,
}

/// <summary>PRD §14.13 — lifecycle of one <see cref="Entities.PeerReview"/> assignment.</summary>
public enum PeerReviewStatus
{
    /// <summary>Assigned to a reviewer, not opened yet.</summary>
    Assigned = 0,
    /// <summary>Reviewer has opened the case but not submitted a score.</summary>
    InProgress = 1,
    /// <summary>Score submitted — unblinds the original author to the reviewer.</summary>
    Completed = 2,
    /// <summary>The original author contests the submitted score; awaits director adjudication.</summary>
    Disputed = 3,
}

/// <summary>
/// PRD §14.14 (TF-004) — teaching-difficulty banding used to filter the
/// teaching library for the right audience (medical student / resident /
/// fellow-level material).
/// </summary>
public enum TeachingDifficulty
{
    Introductory = 0,
    Intermediate = 1,
    Advanced = 2,
}

/// <summary>
/// PRD §14.14 (TF-007) — per-tenant private teaching library with opt-in
/// sharing. <see cref="Private"/> cases are visible only to their author;
/// <see cref="Tenant"/> cases are published to everyone in the same tenant.
/// Cross-tenant / public sharing is deliberately NOT modelled here — nothing
/// in this enum can widen a case beyond its owning tenant.
/// </summary>
public enum TeachingVisibility
{
    Private = 0,
    Tenant = 1,
}
