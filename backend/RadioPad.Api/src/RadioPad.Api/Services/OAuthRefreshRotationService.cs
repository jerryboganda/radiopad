using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Application.Services.Kms;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-35 PROV-007 — wakes every 15 minutes and rotates OAuth refresh
/// tokens whose policy + expiry indicate they are due. The service is a
/// no-op when the registered <see cref="IOAuthTokenIssuer"/> reports
/// <c>CanRefresh = false</c> (the default <c>NoopOAuthTokenIssuer</c>).
/// Successful rotations audit <see cref="AuditAction.OAuthRefreshRotated"/>
/// with <c>kind = "rotated"</c>; failures audit
/// <see cref="AuditAction.ProviderBlocked"/> with the failure reason.
/// </summary>
public sealed class OAuthRefreshRotationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OAuthRefreshRotationService> _log;

    public OAuthRefreshRotationService(
        IServiceScopeFactory scopeFactory,
        ILogger<OAuthRefreshRotationService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OAuth refresh rotation scan failed; will retry.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Single scan pass. Public so integration tests can drive it
    /// deterministically without waiting for the timer.
    /// </summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IOAuthTokenIssuer>();
        if (!issuer.CanRefresh) return 0;

        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<OAuthRefreshVault>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var now = DateTimeOffset.UtcNow;
        // Cross-tenant sweep is intentional: this is a singleton
        // BackgroundService that owns the rotation cadence for every
        // tenant. Tenant isolation is preserved further down — each
        // candidate's TenantId is re-resolved before the per-tenant
        // KEK is fetched and the audit row is written. No request-
        // scoped tenant context exists here, so TenantedController.
        // ResolveContextAsync is deliberately not used.
        // Pull only candidates that have a stored token; final policy gate
        // happens in-memory via OAuthRefreshVault.ShouldRotate.
        var candidates = await db.Providers
            .Where(p => p.OAuthRefreshTokenEnc != null)
            .ToListAsync(ct);

        var rotated = 0;
        foreach (var provider in candidates)
        {
            if (!OAuthRefreshVault.ShouldRotate(provider, now)) continue;

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == provider.TenantId, ct);
            if (tenant is null) continue;

            string keyRef;
            try
            {
                var settings = await db.TenantSettings
                    .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
                keyRef = OAuthRefreshVault.ResolveKekRef(settings?.CmkKeyRef);
            }
            catch (KmsUnavailableException ex)
            {
                await audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenant.Id,
                    Action = AuditAction.ProviderBlocked,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        kind = "rotation_failed",
                        providerId = provider.Id,
                        reason = "kek_unavailable",
                        message = ex.Message,
                    }),
                }, ct);
                continue;
            }

            try
            {
                var ok = await vault.RotateAsync(tenant, provider, keyRef, issuer, ct);
                if (ok)
                {
                    await db.SaveChangesAsync(ct);
                    await audit.AppendAsync(new AuditEvent
                    {
                        TenantId = tenant.Id,
                        Action = AuditAction.OAuthRefreshRotated,
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            kind = "rotated",
                            providerId = provider.Id,
                            expiresAt = provider.OAuthRefreshTokenExpiresAt,
                            rotationPolicy = provider.OAuthRefreshTokenRotationPolicy,
                        }),
                    }, ct);
                    rotated++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OAuth refresh rotation failed for provider {ProviderId}", provider.Id);
                await audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenant.Id,
                    Action = AuditAction.ProviderBlocked,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        kind = "rotation_failed",
                        providerId = provider.Id,
                        reason = ex.GetType().Name,
                    }),
                }, ct);
            }
        }
        return rotated;
    }
}
