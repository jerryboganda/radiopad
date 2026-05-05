using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Stripe;
using Stripe.Checkout;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD BILL-001..006 — Stripe billing surface. Endpoints:
/// <list type="bullet">
///   <item><c>POST /api/billing/checkout</c> — start a Stripe Checkout
///   session (returns the redirect URL). Adds a 14-day trial when the
///   tenant is on the Trial plan and has no Stripe subscription yet.</item>
///   <item><c>POST /api/billing/portal</c> — open a Stripe customer portal
///   session for self-service plan/billing-detail management.</item>
///   <item><c>POST /api/billing/webhook</c> — Stripe-signed webhook receiver
///   that updates <see cref="TenantSettings"/> with the active plan +
///   subscription state via <see cref="SubscriptionLifecycleService"/>.
///   Idempotent on Stripe <c>Event.Id</c> via the
///   <see cref="StripeWebhookEvent"/> table. Untenanted; signature validated
///   against <see cref="BillingEnv.WebhookSecret"/>.</item>
///   <item><c>GET /api/billing/invoices</c> — recent invoices for the
///   tenant's Stripe customer (thin DTO, no PII passthrough).</item>
///   <item><c>POST /api/billing/refund</c> — issue a refund against a
///   payment intent.</item>
///   <item><c>GET /api/billing/status</c> — read-only snapshot of plan +
///   lifecycle markers, used by frontend banners.</item>
/// </list>
/// API keys live in env vars (<c>RADIOPAD_STRIPE_SECRET_KEY</c>,
/// <c>RADIOPAD_STRIPE_WEBHOOK_SECRET</c>) read through
/// <see cref="BillingEnv"/> so they never appear in source.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IBillingAudit _audit;
    private readonly SubscriptionLifecycleService _lifecycle;
    private readonly PlanQuotaService _quota;
    private static readonly HashSet<string> ValidRefundReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "duplicate",
        "fraudulent",
        "requested_by_customer",
    };

    public BillingController(
        RadioPadDbContext db,
        IConfiguration cfg,
        IBillingAudit audit,
        SubscriptionLifecycleService lifecycle,
        PlanQuotaService quota)
    {
        _db = db;
        _cfg = cfg;
        _audit = audit;
        _lifecycle = lifecycle;
        _quota = quota;
        var key = BillingEnv.SecretKey;
        if (!string.IsNullOrWhiteSpace(key)) StripeConfiguration.ApiKey = key;
    }

    public record CheckoutDto(string PriceId, string SuccessUrl, string CancelUrl);
    public record PortalDto(string ReturnUrl);
    public record RefundDto(string PaymentIntentId, long? AmountCents, string? Reason);
    public record CreditUsageDto(long Calls, long InputTokens, long OutputTokens);
    public record CreditLimitsDto(int Calls, int InputTokens, int OutputTokens);
    public record CreditsDto(
        string Plan,
        DateTimeOffset PeriodStart,
        DateTimeOffset PeriodEnd,
        CreditUsageDto Used,
        CreditLimitsDto Limits,
        CreditUsageDto Remaining,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? TrialEndsAt);

    /// <summary>
    /// PRD §11.4 — declarative plan-feature flags. Frontend gates UI surfaces
    /// (marketplace publishing, SCIM, SIEM export, advanced analytics) on
    /// these flags so a Trial tenant cannot accidentally walk into an
    /// enterprise-only flow. Tenant-scoped read.
    /// </summary>
    [HttpGet("features")]
    public async Task<IActionResult> Features(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var plan = settings?.Plan ?? TenantPlan.Trial;
        return Ok(new
        {
            plan = plan.ToString(),
            features = new
            {
                scim = plan == TenantPlan.Enterprise,
                siemExport = plan == TenantPlan.Enterprise,
                marketplacePublish = plan != TenantPlan.Trial,
                advancedAnalytics = plan != TenantPlan.Trial,
                stripeConnect = plan == TenantPlan.Enterprise,
                customKms = plan == TenantPlan.Enterprise,
                ipAllowlist = plan == TenantPlan.Enterprise,
                priorCompare = true,
                voiceDictation = true,
                mcpReadOnly = true,
            },
        });
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe is not configured (RADIOPAD_STRIPE_SECRET_KEY missing).", kind = "provider_unavailable" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        var addTrial = settings.Plan == TenantPlan.Trial && string.IsNullOrEmpty(settings.StripeSubscriptionId);

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = dto.SuccessUrl,
            CancelUrl = dto.CancelUrl,
            Customer = string.IsNullOrEmpty(settings.StripeCustomerId) ? null : settings.StripeCustomerId,
            CustomerEmail = string.IsNullOrEmpty(settings.StripeCustomerId) ? user.Email : null,
            ClientReferenceId = tenant.Id.ToString(),
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = dto.PriceId, Quantity = 1 },
            },
            Metadata = new Dictionary<string, string>
            {
                ["radiopadFlow"] = "billing",
                ["tenantId"] = tenant.Id.ToString(),
                ["tenantSlug"] = tenant.Slug,
            },
        };
        if (addTrial)
        {
            options.SubscriptionData = new SessionSubscriptionDataOptions { TrialPeriodDays = 14 };
        }

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = IdemKey(tenant.Id, "checkout", dto.PriceId, dto.SuccessUrl, dto.CancelUrl),
        };

        await _audit.AppendAsync(tenant.Id, user.Id, "checkout.start", new
        {
            customerId = settings.StripeCustomerId,
            priceId = dto.PriceId,
        }, ct);

        var session = await new SessionService().CreateAsync(options, requestOptions, ct);
        return Ok(new { id = session.Id, url = session.Url });
    }

    [HttpPost("portal")]
    public async Task<IActionResult> Portal([FromBody] PortalDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe is not configured.", kind = "provider_unavailable" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.StripeCustomerId))
            return BadRequest(new { error = "Tenant has no Stripe customer yet.", kind = "validation" });

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = IdemKey(tenant.Id, "portal", settings.StripeCustomerId, dto.ReturnUrl),
        };

        await _audit.AppendAsync(tenant.Id, user.Id, "portal.open", new
        {
            customerId = settings.StripeCustomerId,
        }, ct);

        var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = settings.StripeCustomerId,
                ReturnUrl = dto.ReturnUrl,
            }, requestOptions, ct);
        return Ok(new { url = session.Url });
    }

    /// <summary>
    /// Stripe webhook receiver. Validates the signature, dedupes by
    /// <c>Event.Id</c> via <see cref="StripeWebhookEvent"/>, and updates
    /// <see cref="TenantSettings"/> through
    /// <see cref="SubscriptionLifecycleService"/>.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var secret = BillingEnv.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Webhook not configured.", kind = "provider_unavailable" });

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        Event evt;
        try
        {
            evt = EventUtility.ConstructEvent(
                json, Request.Headers["Stripe-Signature"], secret, 300, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Invalid Stripe signature: {ex.Message}", kind = "validation" });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var existing = await _db.StripeWebhookEvents
            .FirstOrDefaultAsync(e => e.Source == "billing" && e.EventId == evt.Id, ct);
        if (existing is not null)
        {
            return Ok(new { received = true, deduped = true });
        }

        _db.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            EventId = evt.Id,
            EventType = evt.Type,
            Source = "billing",
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var insertedByConcurrentDelivery = await _db.StripeWebhookEvents.AsNoTracking()
                .AnyAsync(e => e.Source == "billing" && e.EventId == evt.Id, ct);
            if (insertedByConcurrentDelivery)
                return Ok(new { received = true, deduped = true });

            throw;
        }

        switch (evt.Type)
        {
            case "checkout.session.completed":
                if (evt.Data.Object is Session session)
                {
                    await UpdateFromSessionAsync(session, ct);
                }
                break;
            case "customer.subscription.updated":
            case "customer.subscription.created":
            case "customer.subscription.deleted":
                if (evt.Data.Object is Subscription sub)
                {
                    await UpdateFromSubscriptionAsync(sub, ct);
                }
                break;
            case "invoice.payment_succeeded":
                if (evt.Data.Object is Invoice paidInvoice)
                {
                    await UpdateFromInvoicePaymentSucceededAsync(paidInvoice, ct);
                }
                break;
            case "invoice.payment_failed":
                if (evt.Data.Object is Invoice failedInvoice)
                {
                    await UpdateFromInvoicePaymentFailedAsync(failedInvoice, ct);
                }
                break;
        }
        await tx.CommitAsync(ct);
        return Ok(new { received = true });
    }

    /// <summary>
    /// PRD BILL-001..006 — recent Stripe invoices for the tenant's customer.
    /// We never pass the raw Stripe object to the client; only a thin DTO.
    /// </summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        if (string.IsNullOrEmpty(BillingEnv.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe is not configured.", kind = "provider_unavailable" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.StripeCustomerId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Tenant has no Stripe customer yet.", kind = "provider_unavailable" });

        var list = await new InvoiceService().ListAsync(new InvoiceListOptions
        {
            Customer = settings.StripeCustomerId,
            Limit = 20,
        }, cancellationToken: ct);

        var dto = list.Data.Select(i => new
        {
            id = i.Id,
            number = i.Number,
            status = i.Status,
            amountDue = i.AmountDue,
            amountPaid = i.AmountPaid,
            currency = i.Currency,
            hostedInvoiceUrl = i.HostedInvoiceUrl,
            invoicePdf = i.InvoicePdf,
            periodStart = i.PeriodStart == default ? (DateTimeOffset?)null : new DateTimeOffset(i.PeriodStart, TimeSpan.Zero),
            periodEnd = i.PeriodEnd == default ? (DateTimeOffset?)null : new DateTimeOffset(i.PeriodEnd, TimeSpan.Zero),
        }).ToArray();
        return Ok(dto);
    }

    /// <summary>
    /// PRD BILL-001..006 — issue a refund against a payment intent. Audit log
    /// hashes the payment-intent id so it never lands in plaintext.
    /// </summary>
    [HttpPost("refund")]
    public async Task<IActionResult> Refund([FromBody] RefundDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;
        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe is not configured.", kind = "provider_unavailable" });
        if (string.IsNullOrWhiteSpace(dto.PaymentIntentId))
            return BadRequest(new { error = "paymentIntentId is required.", kind = "validation" });
        if (dto.AmountCents is <= 0)
            return BadRequest(new { error = "amountCents must be greater than zero when provided.", kind = "validation" });
        if (!string.IsNullOrWhiteSpace(dto.Reason) && !ValidRefundReasons.Contains(dto.Reason))
            return BadRequest(new { error = "reason must be duplicate, fraudulent, or requested_by_customer.", kind = "validation" });

        var settings = await _db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.StripeCustomerId))
            return StatusCode(StatusCodes.Status409Conflict,
                new { error = "Tenant has no Stripe customer to validate refund ownership.", kind = "validation" });

        var paymentIntent = await new PaymentIntentService().GetAsync(dto.PaymentIntentId, cancellationToken: ct);
        if (!PaymentIntentBelongsToTenant(paymentIntent, tenant, settings))
        {
            await _audit.AppendAsync(tenant.Id, user.Id, "refund.rejected", new
            {
                paymentIntentId = dto.PaymentIntentId,
                reason = "tenant_mismatch",
            }, ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Payment intent does not belong to this tenant.", kind = "forbidden" });
        }

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = IdemKey(tenant.Id, "refund",
                dto.PaymentIntentId,
                (dto.AmountCents ?? 0).ToString()),
        };

        var refund = await new RefundService().CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = dto.PaymentIntentId,
            Amount = dto.AmountCents,
            Reason = dto.Reason,
        }, requestOptions, ct);

        await _audit.AppendAsync(tenant.Id, user.Id, "refund.create", new
        {
            paymentIntentId = dto.PaymentIntentId,
            refundId = refund.Id,
            amountCents = refund.Amount,
            reason = dto.Reason,
            status = refund.Status,
        }, ct);

        return Ok(new { id = refund.Id, status = refund.Status, amount = refund.Amount });
    }

    /// <summary>
    /// Iter-30 (BILL-003) — Enterprise invoice bulk export. Streams a CSV
    /// directly when <c>format=csv</c>, or builds an in-memory ZIP archive
    /// (CSV + per-invoice PDFs + SHA-256 manifest) when <c>format=zip</c>.
    /// RBAC: BillingAdmin, ItAdmin, or MedicalDirector.
    /// </summary>
    [HttpGet("invoices/export")]
    public async Task<IActionResult> InvoicesExport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        if (string.IsNullOrEmpty(BillingEnv.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Stripe is not configured.", kind = "provider_unavailable" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.StripeCustomerId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Tenant has no Stripe customer yet.", kind = "provider_unavailable" });

        var listOpts = new InvoiceListOptions
        {
            Customer = settings.StripeCustomerId,
            Limit = 100,
        };
        if (from is not null || to is not null)
        {
            listOpts.Created = new Stripe.DateRangeOptions
            {
                GreaterThanOrEqual = from is { } f
                    ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).UtcDateTime
                    : null,
                LessThanOrEqual = to is { } t
                    ? new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).UtcDateTime
                    : null,
            };
        }

        var list = await new InvoiceService().ListAsync(listOpts, cancellationToken: ct);
        var rows = list.Data.Select(i => new RadioPad.Application.Services.BulkInvoiceRow(
            Id: i.Id ?? "",
            Number: i.Number ?? "",
            Period: $"{(i.PeriodStart == default ? "" : i.PeriodStart.ToString("yyyy-MM-dd"))}/{(i.PeriodEnd == default ? "" : i.PeriodEnd.ToString("yyyy-MM-dd"))}",
            AmountCents: i.AmountDue,
            Currency: i.Currency ?? "",
            Status: i.Status ?? "",
            HostedInvoiceUrl: i.HostedInvoiceUrl ?? "")).ToList();

        await _audit.AppendAsync(tenant.Id, user.Id, "bulk_export", new
        {
            from, to, format,
            invoiceCount = rows.Count,
        }, ct);

        if (string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase))
        {
            var zip = RadioPad.Application.Services.BillingInvoiceExporter.BuildZip(rows, tenant.Slug);
            return File(zip.ZipBytes, "application/zip", $"invoices-{tenant.Slug}.zip");
        }
        var csv = RadioPad.Application.Services.BillingInvoiceExporter.BuildCsv(rows);
        return File(csv, "text/csv", $"invoices-{tenant.Slug}.csv");
    }

    /// <summary>
    /// PRD BILL-001..006 — read-only billing snapshot used by the frontend
    /// dashboard banners (trial countdown, dunning warning, suspended state).
    /// Contains no PII.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var plan = settings?.Plan ?? TenantPlan.Trial;
        return Ok(new
        {
            plan = plan.ToString(),
            subscriptionStatus = string.IsNullOrEmpty(settings?.StripeSubscriptionStatus) ? "none" : settings!.StripeSubscriptionStatus,
            trialEndsAt = settings?.TrialEndsAt,
            gracePeriodUntil = settings?.GracePeriodUntil,
            suspendedAt = settings?.SuspendedAt,
            currentPeriodEnd = settings?.StripeCurrentPeriodEnd,
            customerConfigured = !string.IsNullOrEmpty(settings?.StripeCustomerId),
        });
    }

    /// <summary>
    /// PRD BILL-002 / BILL-007 — read-only month-to-date AI credit balance
    /// (calls + input/output tokens) against the tenant's plan limits, plus
    /// the trial-end timestamp when the tenant is still on the Trial plan.
    /// Reuses <see cref="PlanQuotaService"/> so the numbers shown in the
    /// admin UI are exactly the ones the AI gateway enforces.
    /// </summary>
    [HttpGet("credits")]
    public async Task<IActionResult> Credits(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct)
            ?? new TenantSettings { TenantId = tenant.Id, Plan = TenantPlan.Trial };

        var result = await _quota.CheckAsync(tenant, settings, ct);
        var periodEnd = result.ResetAt;
        var periodStart = periodEnd.AddMonths(-1);
        var limits = PlanLimits.For(result.Plan);
        return Ok(new CreditsDto(
            Plan: result.Plan.ToString(),
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            Used: new CreditUsageDto(result.AiCallsUsed, result.InputTokensUsed, result.OutputTokensUsed),
            Limits: new CreditLimitsDto(limits.AiCallsPerMonth, limits.InputTokensPerMonth, limits.OutputTokensPerMonth),
            Remaining: new CreditUsageDto(
                Math.Max(0, (long)limits.AiCallsPerMonth - result.AiCallsUsed),
                Math.Max(0L, (long)limits.InputTokensPerMonth - result.InputTokensUsed),
                Math.Max(0L, (long)limits.OutputTokensPerMonth - result.OutputTokensUsed)),
            TrialEndsAt: settings.TrialEndsAt));
    }

    private async Task UpdateFromSessionAsync(Session session, CancellationToken ct)
    {
        if (session.Metadata?.TryGetValue("radiopadFlow", out var flow) == true && flow != "billing") return;
        if (!Guid.TryParse(session.ClientReferenceId, out var tenantId)) return;
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return;
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = tenantId };
            _db.TenantSettings.Add(settings);
        }
        if (!string.IsNullOrEmpty(session.CustomerId)) settings.StripeCustomerId = session.CustomerId;
        if (!string.IsNullOrEmpty(session.SubscriptionId)) settings.StripeSubscriptionId = session.SubscriptionId;
        settings.StripeSubscriptionStatus = "active";
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(settings.TenantId, null, "checkout.completed", new
        {
            subscriptionId = session.SubscriptionId,
            customerId = session.CustomerId,
            plan = settings.Plan.ToString(),
        }, ct);
    }

    private async Task UpdateFromSubscriptionAsync(Subscription sub, CancellationToken ct)
    {
        // Identify tenant via the customer id we stored at checkout time.
        var settings = await ResolveSingleSettingsByStripeCustomerAsync(
            sub.CustomerId, "customer.subscription", ct);
        if (settings is null) return;
        settings.StripeSubscriptionId = sub.Id;
        settings.StripeCurrentPeriodEnd = sub.CurrentPeriodEnd == default
            ? null
            : new DateTimeOffset(sub.CurrentPeriodEnd, TimeSpan.Zero);

        var trialEnd = sub.TrialEnd is null || sub.TrialEnd == default(DateTime)
            ? (DateTimeOffset?)null
            : new DateTimeOffset(sub.TrialEnd.Value, TimeSpan.Zero);
        _lifecycle.Apply(settings, sub.Status, trialEnd, DateTimeOffset.UtcNow);

        // Plan tier inferred from the first item's price metadata "plan".
        var plan = sub.Items?.Data.FirstOrDefault()?.Price?.Metadata?.GetValueOrDefault("plan");
        if (Enum.TryParse<TenantPlan>(plan, ignoreCase: true, out var parsed)) settings.Plan = parsed;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(settings.TenantId, null, "subscription.updated", new
        {
            subscriptionId = sub.Id,
            status = sub.Status,
            plan = settings.Plan.ToString(),
        }, ct);
    }

    /// <summary>
    /// Iter-36 — Stripe <c>invoice.payment_succeeded</c>. Clears any open
    /// dunning window and lifts a soft suspension. Marks the subscription
    /// active so the frontend banner clears immediately. Persists through
    /// the same outer transaction the webhook handler opened.
    /// </summary>
    private async Task UpdateFromInvoicePaymentSucceededAsync(Invoice inv, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(inv.CustomerId)) return;
        var settings = await ResolveSingleSettingsByStripeCustomerAsync(
            inv.CustomerId, "invoice.payment_succeeded", ct);
        if (settings is null) return;

        settings.GracePeriodUntil = null;
        settings.SuspendedAt = null;
        settings.StripeSubscriptionStatus = "active";
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(settings.TenantId, null, "invoice.payment_succeeded", new
        {
            invoiceId = inv.Id,
            customerId = inv.CustomerId,
            subscriptionId = inv.SubscriptionId,
            amountPaid = inv.AmountPaid,
            currency = inv.Currency,
        }, ct);
    }

    /// <summary>
    /// Iter-36 — Stripe <c>invoice.payment_failed</c>. First failure opens a
    /// 7-day dunning window via <see cref="SubscriptionLifecycleService.MarkPaymentFailed"/>;
    /// a second failure once that window has elapsed escalates to a hard
    /// suspension. Audit row is appended through <see cref="IBillingAudit"/>
    /// so PII (customer / subscription / invoice ids) is hashed.
    /// </summary>
    private async Task UpdateFromInvoicePaymentFailedAsync(Invoice inv, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(inv.CustomerId)) return;
        var settings = await ResolveSingleSettingsByStripeCustomerAsync(
            inv.CustomerId, "invoice.payment_failed", ct);
        if (settings is null) return;

        var now = DateTimeOffset.UtcNow;
        _lifecycle.MarkPaymentFailed(settings, now);
        settings.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(settings.TenantId, null, "invoice.payment_failed", new
        {
            invoiceId = inv.Id,
            customerId = inv.CustomerId,
            subscriptionId = inv.SubscriptionId,
            amountDue = inv.AmountDue,
            currency = inv.Currency,
            gracePeriodUntil = settings.GracePeriodUntil,
            suspendedAt = settings.SuspendedAt,
        }, ct);
    }

    private async Task<TenantSettings?> ResolveSingleSettingsByStripeCustomerAsync(
        string? customerId,
        string eventType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return null;

        var matches = await _db.TenantSettings
            .Where(s => s.StripeCustomerId == customerId)
            .OrderBy(s => s.TenantId)
            .ToListAsync(ct);

        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0) return null;

        foreach (var settings in matches)
        {
            await _audit.AppendAsync(settings.TenantId, null, "stripe.customer_id_ambiguous", new
            {
                eventType,
                customerId,
                matchCount = matches.Count,
            }, ct);
        }

        return null;
    }

    private static string IdemKey(Guid tenantId, string op, params string[] parts)
        => $"radiopad-{tenantId}-{op}-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join("|", parts))))
            .ToLowerInvariant()[..16];

    private static bool PaymentIntentBelongsToTenant(PaymentIntent paymentIntent, Tenant tenant, TenantSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.StripeCustomerId)
            && string.Equals(paymentIntent.CustomerId, settings.StripeCustomerId, StringComparison.Ordinal))
        {
            return true;
        }

        return paymentIntent.Metadata is not null
            && paymentIntent.Metadata.TryGetValue("tenantId", out var tenantId)
            && string.Equals(tenantId, tenant.Id.ToString(), StringComparison.Ordinal);
    }
}
