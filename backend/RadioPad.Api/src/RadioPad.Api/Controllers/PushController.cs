using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Push;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD MOB-007 — mobile push device registration + admin test send. Tokens
/// are scoped to (tenant, user) and never appear verbatim in audit details.
/// </summary>
[ApiController]
[Route("api/push")]
public class PushController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly PushSenderRegistry _senders;

    public PushController(RadioPadDbContext db, IAuditLog audit, PushSenderRegistry senders)
    {
        _db = db;
        _audit = audit;
        _senders = senders;
    }

    public record RegisterDeviceDto(string Token, string Platform);

    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.Platform))
            return ValidationProblem("token and platform are required.");
        var platform = dto.Platform.ToLowerInvariant();
        if (platform != "ios" && platform != "android" && platform != "web")
            return ValidationProblem("platform must be one of: ios, android, web.");

        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var existing = await _db.PushDevices.FirstOrDefaultAsync(
            d => d.TenantId == tenant.Id && d.UserId == user.Id && d.Token == dto.Token, ct);

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new PushDevice
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Platform = platform,
                Token = dto.Token,
                RegisteredAt = now,
                LastSeenAt = now,
            };
            _db.PushDevices.Add(existing);
        }
        else
        {
            existing.Platform = platform;
            existing.LastSeenAt = now;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PushDeviceRegistered,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                deviceId = existing.Id,
                platform,
                tokenHash = HashToken(dto.Token),
            }),
        }, ct);

        return Ok(new { id = existing.Id, platform });
    }

    [HttpDelete("devices/{token}")]
    public async Task<IActionResult> UnregisterDevice([FromRoute] string token, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var dev = await _db.PushDevices.FirstOrDefaultAsync(
            d => d.TenantId == tenant.Id && d.UserId == user.Id && d.Token == token, ct);
        if (dev is null) return NotFound();

        _db.PushDevices.Remove(dev);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PushDeviceUnregistered,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                deviceId = dev.Id,
                platform = dev.Platform,
                tokenHash = HashToken(token),
            }),
        }, ct);

        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin);
        if (deny is not null) return deny;

        var device = await _db.PushDevices
            .Where(d => d.TenantId == tenant.Id && d.UserId == user.Id)
            .OrderByDescending(d => d.LastSeenAt)
            .FirstOrDefaultAsync(ct);
        if (device is null) return NotFound(new { kind = "no_device", error = "No registered push device for this user." });

        var sender = _senders.Resolve(device.Platform);
        if (sender is null)
            return PushNotConfigured($"No sender registered for platform '{device.Platform}'.");
        if (!sender.IsConfigured)
            return PushNotConfigured($"Provider for '{device.Platform}' is missing required environment variables.");

        try
        {
            await sender.SendAsync(device.Token,
                new PushPayload("RadioPad", "You have a new notification", "test", device.Id.ToString()),
                ct);
        }
        catch (PushNotConfiguredException ex)
        {
            return PushNotConfigured(ex.Message);
        }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PushDeviceTested,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                deviceId = device.Id,
                platform = device.Platform,
                tokenHash = HashToken(device.Token),
            }),
        }, ct);

        return Ok(new { sent = true, deviceId = device.Id, platform = device.Platform });
    }
    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sha256:" + Convert.ToBase64String(bytes, 0, Math.Min(32, bytes.Length));
    }

    private IActionResult PushNotConfigured(string detail)
    {
        var pd = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "push provider not configured",
            Detail = detail,
            Type = "about:blank",
        };
        pd.Extensions["kind"] = "push_not_configured";
        return new ObjectResult(pd) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }
}
