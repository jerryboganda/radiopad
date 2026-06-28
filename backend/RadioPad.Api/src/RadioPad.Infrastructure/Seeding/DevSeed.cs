using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>Seeds a dev tenant, dev radiologist, sandbox + mock providers,
/// the bundled starter rulebooks and report templates so a freshly cloned repo
/// (and the bundled desktop) runs out of the box.</summary>
public static class DevSeed
{
    public static async Task EnsureSeededAsync(RadioPadDbContext db, string rulebooksDir, string templatesDir, CancellationToken ct)
    {
        // Apply migrations when present; fall back to EnsureCreated for the
        // dev-friendly path before the first migration is added.
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any() ||
            (await db.Database.GetAppliedMigrationsAsync(ct)).Any())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "dev", ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Slug = "dev",
                DisplayName = "RadioPad Dev Tenant",
                RequirePhiApprovedProvider = false,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Users.AnyAsync(u => u.TenantId == tenant.Id, ct))
        {
            db.Users.Add(new User
            {
                TenantId = tenant.Id,
                Email = "radiologist@radiopad.local",
                DisplayName = "Dev Radiologist",
                // MedicalDirector, not plain Radiologist: the bundled desktop is a
                // single-user, loopback-only instance where the one operator is BOTH
                // the reporting clinician and the local admin. MedicalDirector is the
                // only role that grants the complete workflow — ReportsEdit + ReportsSign
                // (draft and sign) AND membership in the UBAG admin role set
                // ({ItAdmin, ReportingAdmin, MedicalDirector, ComplianceReviewer}) that
                // gates GET /api/ubag/status and POST /api/ubag/jobs, plus ProvidersRead
                // and AuditRead. A plain Radiologist is NOT in that set, so the UBAG Hub
                // would 403 on a fresh install.
                Role = UserRole.MedicalDirector,
                PasswordHash = "dev",
            });
        }
        await db.SaveChangesAsync(ct);
        await EnterpriseIdentityBridge.EnsureForAllUsersAsync(db, ct);

        if (!await db.Providers.AnyAsync(p => p.TenantId == tenant.Id, ct))
        {
            db.Providers.AddRange(
                new ProviderConfig
                {
                    TenantId = tenant.Id,
                    Name = "Mock (offline)",
                    Adapter = "mock",
                    Model = "mock-1",
                    Compliance = ProviderComplianceClass.Sandbox,
                    Enabled = true,
                    Priority = 100,
                },
                new ProviderConfig
                {
                    TenantId = tenant.Id,
                    Name = "Anthropic (BYOK)",
                    Adapter = "anthropic",
                    Model = "claude-3-5-sonnet-latest",
                    ApiKeySecretRef = "env:ANTHROPIC_API_KEY",
                    Compliance = ProviderComplianceClass.DeIdentifiedOnly,
                    Enabled = true,
                    Priority = 50,
                },
                new ProviderConfig
                {
                    TenantId = tenant.Id,
                    Name = "Local Ollama",
                    Adapter = "ollama-chat",
                    Model = "llama3.1:8b-instruct",
                    EndpointUrl = "http://127.0.0.1:11434",
                    Compliance = ProviderComplianceClass.LocalOnly,
                    Enabled = false,
                    Priority = 10,
                });

            // UBAG browser-automation AI gateway (Gemini Web + DeepSeek Web). These
            // curated primaries now live in UbagPrimarySeed — the single source of
            // truth shared by org bootstrap/registration and the startup backfill, so
            // EVERY org gets them (not just this dev tenant) and the definitions never
            // drift across call sites. The desktop reaches the gateway via the
            // web-server passthrough (RADIOPAD_UBAG_BASE_URL -> /api/ubag-gw);
            // EndpointUrl/ApiKeySecretRef stay empty — UbagClient gets the base URL +
            // bearer from the RADIOPAD_UBAG_* environment. Gemini is the unattended
            // PRIMARY (higher Quality); DeepSeek is the enabled secondary.
            db.Providers.AddRange(UbagPrimarySeed.CuratedPrimaries(tenant.Id));
        }

        if (Directory.Exists(rulebooksDir))
        {
            foreach (var path in Directory.EnumerateFiles(rulebooksDir, "*.yaml"))
            {
                // Per-file resilience: a single malformed bundled rulebook must never
                // abort seeding (and thereby crash sidecar startup). Skip + continue.
                try
                {
                    var yaml = await File.ReadAllTextAsync(path, ct);
                    var spec = RadioPad.Validation.Rulebook.RulebookSpec.FromYaml(yaml);
                    if (string.IsNullOrEmpty(spec.RulebookId)) continue;
                    var existing = await db.Rulebooks.FirstOrDefaultAsync(
                        r => r.TenantId == tenant.Id && r.RulebookId == spec.RulebookId && r.Version == spec.Version, ct);
                    if (existing is not null) continue;
                    db.Rulebooks.Add(new Rulebook
                    {
                        TenantId = tenant.Id,
                        RulebookId = spec.RulebookId,
                        Name = spec.Name,
                        Version = spec.Version,
                        Owner = spec.Owner,
                        Status = ParseStatus(spec.Status),
                        SourceYaml = yaml,
                        CompiledJson = System.Text.Json.JsonSerializer.Serialize(spec),
                        AppliesToModalities = string.Join(',', spec.AppliesTo.Modalities),
                        AppliesToBodyParts = string.Join(',', spec.AppliesTo.BodyParts),
                    });
                }
                catch
                {
                    // Malformed/unsupported rulebook file — skip it, keep seeding the rest.
                }
            }
        }

        // Bundled report templates (templates/*.json). Mirrors the rulebook loop:
        // idempotent (only-when-missing, keyed on TemplateId like the /api/templates
        // POST handler), and per-file try/catch so one malformed template can't abort
        // sidecar startup. The whole `sections` array is preserved verbatim as the
        // SectionsJson blob the editor consumes.
        //
        // The bundled set carries two historical shapes: chest-ct/brain-mri use
        // "templateId" + "subspecialty", while abdomen-us/knee-xray/lumbar-spine-mri use
        // "id" (no subspecialty). Accept either identifier so every bundled template seeds.
        if (Directory.Exists(templatesDir))
        {
            foreach (var path in Directory.EnumerateFiles(templatesDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path, ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                    string Str(string name) =>
                        root.TryGetProperty(name, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
                            ? e.GetString()!
                            : "";

                    var templateId = Str("templateId");
                    if (string.IsNullOrEmpty(templateId)) templateId = Str("id");
                    if (string.IsNullOrEmpty(templateId)) continue;

                    var existing = await db.Templates.FirstOrDefaultAsync(
                        t => t.TenantId == tenant.Id && t.TemplateId == templateId, ct);
                    if (existing is not null) continue;

                    db.Templates.Add(new ReportTemplate
                    {
                        TenantId = tenant.Id,
                        TemplateId = templateId,
                        Name = Str("name"),
                        Modality = Str("modality"),
                        BodyPart = Str("bodyPart"),
                        Subspecialty = Str("subspecialty"),
                        SectionsJson = root.TryGetProperty("sections", out var sEl)
                            ? sEl.GetRawText()
                            : "[]",
                    });
                }
                catch
                {
                    // Malformed/unsupported template file — skip it, keep seeding the rest.
                }
            }
        }

        await db.SaveChangesAsync(ct);

        // Iter-36 — admin-managed Modality + BodyPart catalogs (formerly hardcoded
        // in the frontend). Idempotent; shared with registration/bootstrap + the
        // startup backfill so every org gets the same defaults.
        await CatalogSeed.EnsureCatalogAsync(db, tenant.Id, ct);
    }

    private static RulebookStatus ParseStatus(string s) => s.ToLowerInvariant() switch
    {
        "approved" => RulebookStatus.Approved,
        "review" or "in_review" or "in-review" => RulebookStatus.InReview,
        "deprecated" => RulebookStatus.Deprecated,
        _ => RulebookStatus.Draft,
    };
}
