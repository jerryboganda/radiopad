using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-35 i18n — tenant-level default UI locale (IETF tag).
/// Read by every authenticated user; written by IT-Admin or Medical
/// Director only. Validation: locale must be one of the supported set
/// (<c>en</c>, <c>es</c>, <c>de</c>, <c>fr</c>, <c>pt</c>, <c>hi</c>).
/// Affects chrome only — clinical content is never translated.
/// </summary>
[ApiController]
[Route("api/tenant/settings/locale")]
public class TenantLocaleController : TenantedController
{
    /// <summary>Locked, additive-only set of supported UI locales.</summary>
    public static readonly string[] SupportedLocales = new[] { "en", "es", "de", "fr", "pt", "hi" };

    private readonly RadioPadDbContext _db;
    public TenantLocaleController(RadioPadDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var s = await _db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (s is null)
        {
            s = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(s);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { locale = string.IsNullOrWhiteSpace(s.Locale) ? "en" : s.Locale, supported = SupportedLocales });
    }

    public record SetLocaleDto(string Locale);

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SetLocaleDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var locale = (dto?.Locale ?? "").Trim().ToLowerInvariant();
        if (!SupportedLocales.Contains(locale))
            return BadRequest(new { error = "locale must be one of " + string.Join("|", SupportedLocales) + ".", kind = "validation" });

        var s = await _db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (s is null)
        {
            s = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(s);
        }
        s.Locale = locale;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { locale = s.Locale });
    }
}

/// <summary>
/// Iter-35 i18n — per-user locale override on the active member.
/// Any tenant member may set their own preferred locale; null clears
/// the override and the UI falls back to <c>TenantSettings.Locale</c>.
/// </summary>
[ApiController]
[Route("api/users/me/locale")]
public class UsersMeLocaleController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public UsersMeLocaleController(RadioPadDbContext db) => _db = db;

    public record SetLocaleDto(string? Locale);

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SetLocaleDto dto, CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var raw = dto?.Locale;
        string? locale;
        if (string.IsNullOrWhiteSpace(raw))
        {
            locale = null;
        }
        else
        {
            locale = raw.Trim().ToLowerInvariant();
            if (!TenantLocaleController.SupportedLocales.Contains(locale))
                return BadRequest(new
                {
                    error = "locale must be one of " + string.Join("|", TenantLocaleController.SupportedLocales) + " or empty.",
                    kind = "validation",
                });
        }
        user.PreferredLocale = locale;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { locale = user.PreferredLocale });
    }
}
