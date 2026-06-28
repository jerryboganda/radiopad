using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Api.Middleware;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Self-serve SaaS onboarding — <c>POST /api/registration/create-organization</c>.
///
/// RadioPad is passwordless, so "register" creates a brand-new tenant
/// (organization) plus its first admin <see cref="User"/> and a
/// <see cref="TenantSettings"/> row, then mints a one-time magic link and
/// emails it. The admin finishes setup by clicking the link, which flows
/// through the existing <c>POST /api/auth/magic-link/consume</c> path — no
/// password is ever created.
///
/// Open organization creation is abuse-sensitive, so it is:
/// <list type="bullet">
///   <item>gated by <c>RADIOPAD_ALLOW_SELF_SIGNUP</c> (off by default in
///   Production, on by default outside Production);</item>
///   <item>rate-limited per-email and per-IP via the shared
///   <see cref="MagicLinkRateLimiter"/>;</item>
///   <item>audited via <see cref="AuditAction.OrganizationCreated"/>.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/registration")]
public class RegistrationController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ILogger<RegistrationController> _log;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailSender _email;

    public RegistrationController(
        RadioPadDbContext db,
        IAuditLog audit,
        ILogger<RegistrationController> log,
        IWebHostEnvironment env,
        IEmailSender email)
    { _db = db; _audit = audit; _log = log; _env = env; _email = email; }

    public record CreateOrgDto(
        string OrganizationName,
        string? Slug,
        string AdminEmail,
        string? AdminName,
        string? CallbackUrl);

    // Slugs reserved for routing / first-party surfaces — never assignable to a tenant.
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "admin", "app", "www", "dev", "login", "logout", "register",
        "signup", "sign-in", "signin", "pair", "devices", "auth", "billing",
        "health", "static", "assets", "public", "radiopad", "support", "help",
        "settings", "account", "system", "root", "null", "undefined",
    };

    private static readonly Regex SlugFormat = new("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex EmailFormat = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    [HttpPost("create-organization")]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrgDto dto, CancellationToken ct)
    {
        if (!SelfSignupAllowed())
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Self-serve sign-up is disabled. Contact your administrator for an invitation.",
                kind = "signup_disabled",
            });

        // --- Validation ---------------------------------------------------
        var orgName = (dto.OrganizationName ?? string.Empty).Trim();
        var adminEmail = (dto.AdminEmail ?? string.Empty).Trim();
        var adminName = string.IsNullOrWhiteSpace(dto.AdminName) ? orgName + " Admin" : dto.AdminName!.Trim();

        if (orgName.Length is < 2 or > 120)
            return BadRequest(new { error = "Organization name must be 2–120 characters.", kind = "validation" });
        if (!EmailFormat.IsMatch(adminEmail) || adminEmail.Length > 254)
            return BadRequest(new { error = "A valid work email is required.", kind = "validation" });

        var slug = string.IsNullOrWhiteSpace(dto.Slug) ? Slugify(orgName) : dto.Slug!.Trim().ToLowerInvariant();
        if (!SlugFormat.IsMatch(slug))
            return BadRequest(new
            {
                error = "Organization address must be 3–40 characters, lowercase letters, numbers, and hyphens.",
                kind = "validation",
                field = "slug",
            });
        if (ReservedSlugs.Contains(slug))
            return Conflict(new { error = "That organization address is reserved. Please choose another.", kind = "slug_taken", field = "slug" });

        // --- Rate limit (shared with magic-link request) ------------------
        var emailKey = adminEmail.ToLowerInvariant();
        var ipKey = IpAllowlistMiddleware.ResolveRemoteIp(HttpContext)?.ToString() ?? "__no_ip";
        var rl = MagicLinkRateLimiter.TryAcquire(emailKey, ipKey);
        if (!rl.Acquired)
        {
            try
            {
                await _audit.AppendAsync(new AuditEvent
                {
                    TenantId = Guid.Empty,
                    Action = AuditAction.RateLimited,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        endpoint = "registration/create-organization",
                        scope = rl.Scope,
                        emailHash = MagicLinkController.Sha256Hex(emailKey),
                        retryAfterSeconds = rl.RetryAfterSeconds,
                    }),
                }, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Registration rate-limit audit append failed.");
            }
            HttpContext.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "Too many sign-up requests. Please try again later.",
                kind = "rate-limit",
            });
        }

        // --- Production deliverability guards (mirror magic-link) ----------
        if (_env.IsProduction() && !EmailConfigured())
        {
            _log.LogError("Self-signup email is not configured in Production.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Sign-up email is not configured.",
                kind = "email_unavailable",
            });
        }
        if (_env.IsProduction() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_PUBLIC_WEB_URL")))
        {
            _log.LogError("RADIOPAD_PUBLIC_WEB_URL is not configured in Production.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Public web URL is not configured.",
                kind = "public_web_url_unconfigured",
            });
        }

        // --- Uniqueness ---------------------------------------------------
        if (await _db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            return Conflict(new { error = "That organization address is already taken.", kind = "slug_taken", field = "slug" });

        // --- Create tenant + first admin + settings -----------------------
        var tenant = new Tenant { Slug = slug, DisplayName = orgName };
        _db.Tenants.Add(tenant);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a slug race to a concurrent signup.
            return Conflict(new { error = "That organization address is already taken.", kind = "slug_taken", field = "slug" });
        }

        var admin = new User
        {
            TenantId = tenant.Id,
            Email = adminEmail,
            DisplayName = adminName,
            // First member of a new org is the local admin AND reporting clinician.
            // MedicalDirector is the only role granting the full workflow plus the
            // admin role-set (see DevSeed rationale).
            Role = UserRole.MedicalDirector,
            IsActive = true,
        };
        _db.Users.Add(admin);
        _db.TenantSettings.Add(new TenantSettings
        {
            TenantId = tenant.Id,
            Plan = TenantPlan.Trial,
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(30),
        });

        var raw = "ml_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        _db.MagicLinks.Add(new MagicLinkToken
        {
            TenantId = tenant.Id,
            UserId = admin.Id,
            TokenHash = MagicLinkController.Sha256Hex(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
        });
        await _db.SaveChangesAsync(ct);

        // Surface the curated UBAG models (Gemini Web + DeepSeek Web) on the new org's
        // AI-models page from day one. Isolated + idempotent: a gateway/seed hiccup must
        // never fail org creation, so swallow + log.
        try { await UbagPrimarySeed.EnsureCuratedPrimariesAsync(_db, tenant.Id, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "UBAG primary seeding failed for new org {Slug}", slug); }

        // Iter-36 — seed the admin Modality + BodyPart catalogs so the new org's
        // reporting module has selectable defaults from day one. Idempotent + isolated.
        try { await CatalogSeed.EnsureCatalogAsync(_db, tenant.Id, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Catalog seeding failed for new org {Slug}", slug); }

        // Bridge the new admin into the enterprise-identity tables so the
        // first magic-link consume mints a session cleanly.
        await EnterpriseIdentityBridge.EnsureMembershipForUserAsync(_db, admin, ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = admin.Id,
            Action = AuditAction.OrganizationCreated,
            DetailsJson = JsonSerializer.Serialize(new { slug, adminEmail, plan = TenantPlan.Trial.ToString() }),
        }, ct);

        // --- Deliver the secure setup link --------------------------------
        var link = BuildMagicLink(ResolveLoginCallback(dto.CallbackUrl), raw);
        var sent = await TrySendWelcomeAsync(adminEmail, orgName, link, ct);
        if (!sent && _env.IsProduction())
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Your organization was created, but the setup email could not be sent. Please use 'Trouble signing in?' to request a new link.",
                kind = "email_unavailable",
                slug,
            });
        }

        if (!sent && RadioPadRequestIdentity.DevHeadersEnabled(HttpContext))
            return Ok(new { ok = true, slug, devLink = link });

        return Ok(new { ok = true, slug });
    }

    private bool SelfSignupAllowed()
    {
        var flag = Environment.GetEnvironmentVariable("RADIOPAD_ALLOW_SELF_SIGNUP");
        var on = string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) || flag == "1";
        var off = string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase) || flag == "0";
        return on || (!_env.IsProduction() && !off);
    }

    internal static string Slugify(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        char prev = '\0';
        foreach (var ch in lower)
        {
            var mapped = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') ? ch : '-';
            if (mapped == '-' && (prev == '-' || prev == '\0')) continue;
            sb.Append(mapped);
            prev = mapped;
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length < 3) slug = (slug + "-org").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug;
    }

    private static bool EmailConfigured() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_GMAIL_REFRESH_TOKEN"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_EMAIL_API_KEY"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_SMTP_HOST"));

    private string ResolveLoginCallback(string? requested)
    {
        if (!_env.IsProduction()
            && Uri.TryCreate(requested, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString().TrimEnd('&', '?');
        }

        var configured = Environment.GetEnvironmentVariable("RADIOPAD_PUBLIC_WEB_URL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var trimmed = configured.TrimEnd('/');
            return trimmed.EndsWith("/login", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + "/login";
        }

        var req = HttpContext?.Request;
        if (req is not null)
        {
            var proto = req.Headers["X-Forwarded-Proto"].FirstOrDefault();
            var scheme = string.IsNullOrWhiteSpace(proto) ? req.Scheme : proto;
            var host = req.Headers["X-Forwarded-Host"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(host)) host = req.Host.Value;
            if (!string.IsNullOrWhiteSpace(host)) return $"{scheme}://{host.TrimEnd('/')}/login";
        }
        return "http://localhost:3000/login";
    }

    private static string BuildMagicLink(string callback, string rawToken)
    {
        var separator = callback.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{callback}{separator}magic={Uri.EscapeDataString(rawToken)}";
    }

    private async Task<bool> TrySendWelcomeAsync(string to, string orgName, string link, CancellationToken ct)
    {
        var plain = $"Welcome to RadioPad,\r\n\r\nYour organization \"{orgName}\" is ready. "
            + "Use this secure link to finish setup and sign in. It expires in 60 minutes and can be used once:\r\n\r\n"
            + link
            + "\r\n\r\nIf you did not create this organization, you can safely ignore this email.\r\n\r\n-- RadioPad\r\n";
        var html = BuildWelcomeHtml(orgName, link);
        var message = new EmailMessage(
            To: to,
            Subject: "Finish setting up your RadioPad organization",
            HtmlBody: html,
            PlainBody: plain);
        return await _email.SendAsync(message, ct);
    }

    private static string BuildWelcomeHtml(string orgName, string link)
    {
        var safeOrg = System.Net.WebUtility.HtmlEncode(orgName);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#faf9f7;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#2b2b2b\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#faf9f7;padding:32px 16px\"><tr><td align=\"center\">");
        sb.Append("<table role=\"presentation\" width=\"560\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;background:#ffffff;border:1px solid #e7e3dc;border-radius:12px;overflow:hidden\">");
        sb.Append("<tr><td style=\"padding:28px 32px 8px 32px\">");
        sb.Append("<div style=\"font-size:13px;letter-spacing:.04em;color:#c96442;text-transform:uppercase;font-weight:600\">RadioPad</div>");
        sb.Append("<h1 style=\"margin:8px 0 0 0;font-size:22px;font-weight:600;color:#1f1f1f\">Welcome — your workspace is ready</h1>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:8px 32px 0 32px;font-size:15px;line-height:1.55;color:#3a3a3a\">");
        sb.Append("<p style=\"margin:12px 0\">Your organization <strong>").Append(safeOrg).Append("</strong> has been created. Click below to finish setup and sign in. The link expires in <strong>60 minutes</strong> and works only once.</p>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:20px 32px 8px 32px\">");
        sb.Append("<a href=\"").Append(link).Append("\" style=\"display:inline-block;background:#c96442;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:12px 22px;border-radius:8px\">Finish setup &amp; sign in</a>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:8px 32px 0 32px;font-size:13px;line-height:1.55;color:#666\">");
        sb.Append("<p style=\"margin:12px 0\">Or paste this link into your browser:</p>");
        sb.Append("<p style=\"margin:6px 0;word-break:break-all;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:12px;color:#444\">").Append(link).Append("</p>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:20px 32px 28px 32px;font-size:12px;line-height:1.55;color:#888;border-top:1px solid #f0ece5\">");
        sb.Append("<p style=\"margin:12px 0 0 0\">If you did not create this organization, you can safely ignore this email -- no action will be taken.</p>");
        sb.Append("</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }
}
