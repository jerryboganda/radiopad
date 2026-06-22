using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Seeding;

/// <summary>Seeds a dev tenant, dev radiologist, sandbox + mock providers,
/// and a starter rulebook so a freshly cloned repo runs out of the box.</summary>
public static class DevSeed
{
    public static async Task EnsureSeededAsync(RadioPadDbContext db, string rulebooksDir, CancellationToken ct)
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
                Role = UserRole.Radiologist,
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
                },
                // UBAG browser-automation AI gateway. The desktop reaches it via the
                // web-server passthrough (RADIOPAD_UBAG_BASE_URL -> /api/ubag-gw);
                // EndpointUrl/ApiKeySecretRef stay empty — UbagClient gets the base
                // URL + bearer from the RADIOPAD_UBAG_* environment.
                //
                // Gemini is the unattended PRIMARY: end-to-end QA (2026-06) showed
                // gemini_web returns clean, final radiology impressions, whereas the
                // gateway's deepseek_web extractor returns the reasoner's chain-of-
                // thought ("The user asks for…") instead of the answer — unusable as a
                // draft until the gateway scraper is fixed. DeepSeek stays enabled as a
                // secondary fallback but is deliberately ranked below Gemini. The router
                // (EfProviderRouter) is Quality/Cost weighted and only uses Priority as a
                // tie-break, so Quality is set explicitly to make Gemini win outright.
                new ProviderConfig
                {
                    TenantId = tenant.Id,
                    Name = "UBAG (Gemini Web)",
                    Adapter = "ubag",
                    Model = "gemini_web",
                    Compliance = ProviderComplianceClass.Sandbox,
                    Enabled = true,
                    Quality = 0.9m,
                    Priority = 1,
                },
                new ProviderConfig
                {
                    TenantId = tenant.Id,
                    Name = "UBAG (DeepSeek Web)",
                    Adapter = "ubag",
                    Model = "deepseek_web",
                    Compliance = ProviderComplianceClass.Sandbox,
                    Enabled = true,
                    Quality = 0.3m,
                    Priority = 2,
                });
        }

        if (Directory.Exists(rulebooksDir))
        {
            foreach (var path in Directory.EnumerateFiles(rulebooksDir, "*.yaml"))
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
        }

        await db.SaveChangesAsync(ct);
    }

    private static RulebookStatus ParseStatus(string s) => s.ToLowerInvariant() switch
    {
        "approved" => RulebookStatus.Approved,
        "review" or "in_review" or "in-review" => RulebookStatus.InReview,
        "deprecated" => RulebookStatus.Deprecated,
        _ => RulebookStatus.Draft,
    };
}
