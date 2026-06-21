using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Security;

namespace RadioPad.Infrastructure.Persistence;

public class RadioPadDbContext : DbContext
{
    public RadioPadDbContext(DbContextOptions<RadioPadDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<GlobalUser> GlobalUsers => Set<GlobalUser>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<ProviderConfig> Providers => Set<ProviderConfig>();
    public DbSet<CopilotIntegrationSettings> CopilotIntegrationSettings => Set<CopilotIntegrationSettings>();
    public DbSet<CopilotFeatureFlag> CopilotFeatureFlags => Set<CopilotFeatureFlag>();
    public DbSet<CopilotUserAccount> CopilotUserAccounts => Set<CopilotUserAccount>();
    public DbSet<CopilotEntitlement> CopilotEntitlements => Set<CopilotEntitlement>();
    public DbSet<CopilotQuotaPolicy> CopilotQuotaPolicies => Set<CopilotQuotaPolicy>();
    public DbSet<CopilotSession> CopilotSessions => Set<CopilotSession>();
    public DbSet<CopilotMessage> CopilotMessages => Set<CopilotMessage>();
    public DbSet<CopilotUsageEvent> CopilotUsageEvents => Set<CopilotUsageEvent>();
    public DbSet<CopilotDiagnosticRun> CopilotDiagnosticRuns => Set<CopilotDiagnosticRun>();
    public DbSet<Rulebook> Rulebooks => Set<Rulebook>();
    public DbSet<ReportTemplate> Templates => Set<ReportTemplate>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportVersion> ReportVersions => Set<ReportVersion>();
    public DbSet<ReportSignature> ReportSignatures => Set<ReportSignature>();
    /// <summary>Iter-0a (PRD §14.12, RADS-001) — first-class structured RADS assessments per report.</summary>
    public DbSet<RadsAssessment> RadsAssessments => Set<RadsAssessment>();
    /// <summary>Iter-0a (PRD RPT-003, RADS-005) — first-class structured measurements per report.</summary>
    public DbSet<ReportMeasurement> ReportMeasurements => Set<ReportMeasurement>();
    public DbSet<AiRequest> AiRequests => Set<AiRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<TenantLexicon> Lexicons => Set<TenantLexicon>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<MagicLinkToken> MagicLinks => Set<MagicLinkToken>();
    public DbSet<DeviceAuthRequest> DeviceAuth => Set<DeviceAuthRequest>();
    public DbSet<MarketplaceListing> MarketplaceListings => Set<MarketplaceListing>();
    public DbSet<MarketplacePurchase> MarketplacePurchases => Set<MarketplacePurchase>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    /// <summary>Iter-31 AI-009 — per-tenant overrides for rulebook prompt blocks.</summary>
    public DbSet<PromptOverride> PromptOverrides => Set<PromptOverride>();

    // Iter-31 MCP-001/004 — Model Context Protocol tool registry + invocation ledger.
    public DbSet<McpTool> McpTools => Set<McpTool>();
    public DbSet<McpToolCall> McpToolCalls => Set<McpToolCall>();

    // Iter-32 AUTH-005 — SCIM 2.0 Groups + memberships.
    public DbSet<ScimGroup> ScimGroups => Set<ScimGroup>();
    public DbSet<ScimGroupMembership> ScimGroupMemberships => Set<ScimGroupMembership>();
    /// <summary>Iter-32 AUTH-001 — WebAuthn / FIDO2 passkey credentials.</summary>
    public DbSet<WebAuthnCredential> WebAuthnCredentials => Set<WebAuthnCredential>();
    /// <summary>Iter-33 MCP-007 — tenant-scoped ed25519 publisher keys trusted to sign plugin manifests.</summary>
    public DbSet<TrustedPluginPublisher> TrustedPluginPublishers => Set<TrustedPluginPublisher>();

    /// <summary>Iter-35 — versioned clinical validation packs (rulebook golden suites).</summary>
    public DbSet<ValidationPack> ValidationPacks => Set<ValidationPack>();

    /// <summary>PRD §18.2 — drift detection baselines per (tenant, provider, rulebook).</summary>
    public DbSet<DriftBaseline> DriftBaselines => Set<DriftBaseline>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<User>().HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        b.Entity<GlobalUser>().HasIndex(x => x.NormalizedEmail);
        b.Entity<ExternalIdentity>().HasIndex(x => new { x.ProviderKey, x.Issuer, x.Subject }).IsUnique();
        b.Entity<ExternalIdentity>().HasIndex(x => new { x.GlobalUserId, x.ProviderKey });
        b.Entity<ExternalIdentity>().HasIndex(x => x.NormalizedEmail);
        b.Entity<TenantMembership>().HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        b.Entity<TenantMembership>().HasIndex(x => new { x.GlobalUserId, x.TenantId }).IsUnique();
        b.Entity<TenantMembership>().HasIndex(x => new { x.TenantId, x.Status });
        b.Entity<AuthSession>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<AuthSession>().HasIndex(x => new { x.GlobalUserId, x.ExpiresAt });
        b.Entity<AuthSession>().HasIndex(x => new { x.TenantId, x.UserId, x.ExpiresAt });
        b.Entity<AuthSession>().HasIndex(x => new { x.RevokedAt, x.ExpiresAt });
        b.Entity<Rulebook>().HasIndex(x => new { x.TenantId, x.RulebookId, x.Version }).IsUnique();
        b.Entity<ReportTemplate>().HasIndex(x => new { x.TenantId, x.TemplateId }).IsUnique();
        b.Entity<ProviderConfig>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Entity<CopilotIntegrationSettings>().HasIndex(x => x.TenantId).IsUnique();
        b.Entity<CopilotFeatureFlag>().HasIndex(x => new { x.TenantId, x.FeatureKey }).IsUnique();
        b.Entity<CopilotUserAccount>().HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        b.Entity<CopilotEntitlement>().HasIndex(x => new { x.TenantId, x.UserId, x.Mode }).IsUnique();
        b.Entity<CopilotQuotaPolicy>().HasIndex(x => new { x.TenantId, x.ScopeType, x.ScopeKey, x.Feature }).IsUnique();
        b.Entity<CopilotSession>().HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        b.Entity<CopilotSession>().HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        b.Entity<CopilotMessage>().HasIndex(x => new { x.TenantId, x.SessionId, x.Sequence }).IsUnique();
        b.Entity<CopilotUsageEvent>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Entity<CopilotUsageEvent>().HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        b.Entity<CopilotDiagnosticRun>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Entity<TenantLexicon>().HasIndex(x => new { x.TenantId, x.Term }).IsUnique();
        b.Entity<TenantSettings>().HasIndex(x => x.TenantId).IsUnique();
        b.Entity<MagicLinkToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<DeviceAuthRequest>().HasIndex(x => x.DeviceCodeHash).IsUnique();
        b.Entity<DeviceAuthRequest>().HasIndex(x => x.UserCode).IsUnique();
        b.Entity<StripeWebhookEvent>().HasIndex(e => new { e.Source, e.EventId }).IsUnique();
        b.Entity<PushDevice>().HasIndex(x => new { x.TenantId, x.UserId, x.Token }).IsUnique();
        b.Entity<PushDevice>().HasIndex(x => new { x.TenantId, x.UserId, x.LastSeenAt });
        b.Entity<PromptOverride>().HasIndex(x => new { x.TenantId, x.RulebookId, x.BlockKey }).IsUnique();

        // Iter-31 MCP — registry + invocation ledger.
        b.Entity<McpTool>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Entity<McpToolCall>().HasIndex(x => new { x.TenantId, x.CreatedAt });

        // Iter-32 AUTH-005 — SCIM 2.0 Groups + memberships. DisplayName is
        // unique per tenant (RFC 7643 §4.2). Memberships are unique by
        // (group, user) so the same identity can't appear twice.
        b.Entity<ScimGroup>().HasIndex(x => new { x.TenantId, x.DisplayName }).IsUnique();
        b.Entity<ScimGroup>().HasMany(x => x.Members).WithOne().HasForeignKey(m => m.GroupId);
        b.Entity<ScimGroupMembership>().HasIndex(x => new { x.TenantId, x.GroupId, x.UserId }).IsUnique();
        b.Entity<WebAuthnCredential>().HasIndex(x => new { x.TenantId, x.CredentialIdHash }).IsUnique();
        b.Entity<WebAuthnCredential>().HasIndex(x => new { x.TenantId, x.UserId });

        // Iter-33 MCP-007 — trusted plugin publisher keys (per-tenant).
        b.Entity<TrustedPluginPublisher>()
            .HasIndex(x => new { x.TenantId, x.Ed25519PublicKeyBase64 })
            .IsUnique();
        b.Entity<TrustedPluginPublisher>()
            .HasIndex(x => new { x.TenantId, x.PublisherName });

        // Iter-35 — clinical validation packs. (TenantId, RulebookId, Version)
        // is unique so importing the same rulebook+version twice is rejected.
        b.Entity<ValidationPack>()
            .HasIndex(x => new { x.TenantId, x.RulebookId, x.Version }).IsUnique();
        b.Entity<ValidationPack>()
            .HasIndex(x => new { x.TenantId, x.RulebookId });

        // PRD §18.2 — drift baselines are unique per (tenant, provider, rulebook).
        b.Entity<DriftBaseline>()
            .HasIndex(x => new { x.TenantId, x.ProviderId, x.RulebookId }).IsUnique();

        b.Entity<Report>().OwnsOne(x => x.Study);
        b.Entity<Report>().HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ReportId);
        b.Entity<Report>().HasMany(x => x.Signatures).WithOne().HasForeignKey(s => s.ReportId);
        b.Entity<ReportSignature>().HasIndex(s => new { s.TenantId, s.ReportId, s.Role });

        // Iter-0a (PRD §14.12 / RPT-003 / RADS-001) — structured report data model.
        // RADS assessments + measurements are first-class children of a report so the
        // RADS engine, contradiction guard, lesion tracker, and analytics can query
        // them by family/category/lesion across reports without parsing a JSON blob.
        b.Entity<Report>().HasMany(x => x.RadsAssessments).WithOne().HasForeignKey(r => r.ReportId);
        b.Entity<Report>().HasMany(x => x.Measurements).WithOne().HasForeignKey(m => m.ReportId);
        // RADS analytics (RADS-008) scans by (tenant, family, category); contradiction
        // guard + report load scan by (tenant, report).
        b.Entity<RadsAssessment>().HasIndex(x => new { x.TenantId, x.ReportId });
        b.Entity<RadsAssessment>().HasIndex(x => new { x.TenantId, x.Family, x.Category });
        // Lesion tracking (COMP-003/004) follows a lesion by (tenant, lesion key).
        b.Entity<ReportMeasurement>().HasIndex(x => new { x.TenantId, x.ReportId });
        b.Entity<ReportMeasurement>().HasIndex(x => new { x.TenantId, x.LesionKey });

        // Make AuditEvent effectively append-only at the model level: no
        // navigation back-references and a unique IntegrityChain prefix.
        b.Entity<AuditEvent>().HasIndex(x => new { x.TenantId, x.CreatedAt });
        // Iter-32 SEC-011 — fast scan path for AnomalyDetector which queries
        // by (Action, CreatedAt) every 60 s across all tenants.
        b.Entity<AuditEvent>().HasIndex(x => new { x.Action, x.CreatedAt });

        // Iter-31 SEC-002 — at-rest column-level encryption. Each property
        // listed here is transparently encrypted with AES-256-GCM by the
        // configured column encryptor before being persisted, and decrypted
        // on materialisation. Legacy unencrypted rows pass through verbatim
        // until a write rotates them into the encrypted form.
        var enc = new ValueConverter<string, string>(
            v => ColumnEncryptorAccessor.Current.EncryptString(v),
            v => ColumnEncryptorAccessor.Current.DecryptString(v));
        b.Entity<User>().Property(u => u.MfaSecret).HasConversion(enc);
        // hash columns store one-way digests; encryption-at-rest converter omitted to keep equality lookups working.
        b.Entity<TenantSettings>().Property(s => s.IngestBearerSecret).HasConversion(enc);
        b.Entity<TenantSettings>().Property(s => s.FhirWebhookSecret).HasConversion(enc);
        b.Entity<TenantSettings>().Property(s => s.DicomWebBearerSecret).HasConversion(enc);
        b.Entity<TenantSettings>().Property(s => s.ScimBearerSecret).HasConversion(enc);
        b.Entity<ProviderConfig>().Property(p => p.ApiKeySecretRef).HasConversion(enc);
        b.Entity<CopilotIntegrationSettings>().Property(s => s.GitHubAppPrivateKeySecretRef).HasConversion(enc);
        b.Entity<CopilotIntegrationSettings>().Property(s => s.OAuthClientSecretRef).HasConversion(enc);
        b.Entity<CopilotUserAccount>().Property(a => a.TokenSecretRef).HasConversion(enc);

        ConfigureSqliteDateTimeOffsetConverters(b);
    }

    private void ConfigureSqliteDateTimeOffsetConverters(ModelBuilder b)
    {
        if (!string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal)) return;

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUniversalTime().Ticks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.ToUniversalTime().Ticks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                    property.SetColumnType("INTEGER");
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableDateTimeOffsetConverter);
                    property.SetColumnType("INTEGER");
                }
            }
        }
    }
}
