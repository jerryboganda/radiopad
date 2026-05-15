using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Stripe;
using Stripe.Checkout;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD §16 (Marketplace) — submission / review / purchase pipeline for
/// community-published rulebooks, templates and prompt packs.
///
/// Lifecycle:
///   <list type="bullet">
///   <item><c>POST /api/marketplace/listings</c> — publisher creates a draft.</item>
///   <item><c>POST /api/marketplace/listings/{id}/submit</c> — moves to <c>submitted</c>; locks edits.</item>
///   <item><c>POST /api/marketplace/listings/{id}/approve</c> — admin approves; creates Stripe Price.</item>
///   <item><c>POST /api/marketplace/listings/{id}/reject</c> — admin rejects with reason.</item>
///   <item><c>GET  /api/marketplace/listings</c> — public catalogue (approved only).</item>
///   <item><c>POST /api/marketplace/listings/{id}/checkout</c> — buyer opens Stripe Checkout (Connect destination charge with rev-share).</item>
///   <item><c>POST /api/marketplace/connect/onboarding</c> — publisher gets a Stripe Connect Express onboarding link.</item>
///   <item><c>POST /api/marketplace/webhook</c> — Stripe webhook flips purchases to <c>paid</c>.</item>
///   </list>
///
/// Revenue share is enforced via Stripe Connect destination charges:
/// <c>application_fee_amount = price * (1 - revenueShareBps/10000)</c> stays
/// with the platform; the remainder is transferred to the publisher's
/// connected account. Free listings (priceCents == 0) skip Stripe entirely.
/// </summary>
[ApiController]
[Route("api/marketplace")]
public class MarketplaceController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IBillingAudit _billingAudit;
    private readonly ILogger<MarketplaceController> _log;
    private readonly IHostEnvironment _env;
    private static readonly HashSet<string> ValidRefundReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "duplicate",
        "fraudulent",
        "requested_by_customer",
    };

    public MarketplaceController(
        RadioPadDbContext db,
        IAuditLog audit,
        IBillingAudit billingAudit,
        ILogger<MarketplaceController> log,
        IHostEnvironment env)
    {
        _db = db;
        _audit = audit;
        _billingAudit = billingAudit;
        _log = log;
        _env = env;
        StripeConfiguration.ApiKey = BillingEnv.SecretKey;
    }

    private static string IdemKey(Guid scope, string op, params string[] parts)
        => $"radiopad-mp-{scope}-{op}-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|", parts)))).ToLowerInvariant()[..16];

    public record ListingDto(string Name, string Description, string Kind, string ArtifactBody, int PriceCents);
    public record RejectDto(string Reason);
    public record MarketplaceRefundDto(string? Reason);
    public record SubmissionDto(string Category, string SourceId, string Version, string? Description);
    public record SubmissionRejectDto(string? ReviewNotes);

    [HttpGet("listings")]
    public async Task<IActionResult> List([FromQuery] string? kind, CancellationToken ct)
    {
        var q = _db.MarketplaceListings.Where(l => l.Status == "approved");
        if (!string.IsNullOrEmpty(kind)) q = q.Where(l => l.Kind == kind);
        var list = await q
            .OrderByDescending(l => l.ReviewedAt)
            .Select(l => new
            {
                l.Id, l.Name, l.Description, l.Kind, l.PriceCents,
                publisher = l.PublisherTenantId, l.ReviewedAt,
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("listings/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null || row.Status != "approved")
            return NotFound(new { error = "listing", kind = "not_found" });
        return Ok(new
        {
            row.Id,
            row.Name,
            row.Description,
            row.Kind,
            row.PriceCents,
            publisher = row.PublisherTenantId,
            row.ReviewedAt,
        });
    }

    [HttpPost("listings")]
    public async Task<IActionResult> Create([FromBody] ListingDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.ArtifactBody))
            return BadRequest(new { error = "name and artifact body required.", kind = "validation" });
        var row = new MarketplaceListing
        {
            PublisherTenantId = tenant.Id,
            PublisherUserId = user.Id,
            Name = dto.Name,
            Description = dto.Description ?? "",
            Kind = string.IsNullOrEmpty(dto.Kind) ? "rulebook" : dto.Kind,
            ArtifactBody = dto.ArtifactBody,
            PriceCents = Math.Max(0, dto.PriceCents),
            Status = "draft",
        };
        _db.MarketplaceListings.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.Status });
    }

    [HttpPost("listings/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id && l.PublisherTenantId == tenant.Id, ct);
        if (row is null) return NotFound(new { error = "listing", kind = "not_found" });
        if (row.Status != "draft") return BadRequest(new { error = $"Cannot submit while {row.Status}.", kind = "validation" });
        row.Status = "submitted";
        row.SubmittedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { row.Status });
    }

    [HttpPost("listings/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (forbid is not null) return forbid;
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id && l.PublisherTenantId == tenant.Id, ct);
        if (row is null) return NotFound(new { error = "listing", kind = "not_found" });
        if (row.Status != "submitted") return BadRequest(new { error = "Only submitted listings can be approved.", kind = "validation" });

        // Create Stripe Price for paid listings if Stripe is configured.
        if (row.PriceCents > 0 && !string.IsNullOrEmpty(StripeConfiguration.ApiKey))
        {
            try
            {
                var product = await new ProductService().CreateAsync(new ProductCreateOptions
                {
                    Name = row.Name,
                    Description = row.Description,
                }, new RequestOptions { IdempotencyKey = IdemKey(row.Id, "product", row.Name ?? "", row.Description ?? "") }, ct);
                var price = await new PriceService().CreateAsync(new PriceCreateOptions
                {
                    Product = product.Id,
                    Currency = "usd",
                    UnitAmount = row.PriceCents,
                }, new RequestOptions { IdempotencyKey = IdemKey(row.Id, "price", product.Id, row.PriceCents.ToString()) }, ct);
                row.StripePriceId = price.Id;
            }
            catch (StripeException ex)
            {
                _log.LogWarning(ex, "Stripe price creation failed; approval continues without StripePriceId.");
            }
        }

        row.Status = "approved";
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewerUserId = user.Id;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.RulebookApproved,
            DetailsJson = JsonSerializer.Serialize(new { marketplaceListing = row.Id, kind = row.Kind }),
        }, ct);
        return Ok(new { row.Status, row.StripePriceId });
    }

    [HttpPost("listings/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (forbid is not null) return forbid;
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id && l.PublisherTenantId == tenant.Id, ct);
        if (row is null) return NotFound(new { error = "listing", kind = "not_found" });
        row.Status = "rejected";
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewerUserId = user.Id;
        row.RejectionReason = dto.Reason;
        await _db.SaveChangesAsync(ct);
        return Ok(new { row.Status });
    }

    [HttpPost("listings/{id:guid}/checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromQuery] string returnUrl, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id && l.Status == "approved", ct);
        if (row is null) return NotFound(new { error = "listing", kind = "not_found" });

        var purchase = new MarketplacePurchase
        {
            ListingId = row.Id,
            BuyerTenantId = tenant.Id,
            BuyerUserId = user.Id,
            AmountCents = row.PriceCents,
            Status = "pending",
        };
        _db.MarketplacePurchases.Add(purchase);

        if (row.PriceCents == 0)
        {
            // Free assets are granted immediately; still record the purchase row.
            purchase.Status = "paid";
            purchase.PaidAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new { granted = true, purchaseId = purchase.Id });
        }

        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey) || string.IsNullOrEmpty(row.StripePriceId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured.", kind = "config" });

        var publisher = await _db.Tenants.FirstAsync(t => t.Id == row.PublisherTenantId, ct);
        if (row.PriceCents > 0)
        {
            var pubSettings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == publisher.Id, ct);
            if (string.IsNullOrEmpty(publisher.StripeConnectAccountId) || pubSettings?.ChargesEnabled != true)
            {
                await _billingAudit.AppendAsync(tenant.Id, user.Id, "marketplace.checkout.blocked",
                    new { listingId = row.Id, reason = "connect_not_ready" }, ct);
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    error = "Publisher Stripe Connect account is not ready (charges_enabled=false).",
                    kind = "connect_not_ready",
                });
            }
        }

        var feeBps = 10_000 - row.RevenueShareBps;
        var feeAmount = (long)Math.Round(row.PriceCents * (feeBps / 10_000.0));

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = new() { new() { Price = row.StripePriceId, Quantity = 1 } },
            SuccessUrl = returnUrl + "?status=success&pid=" + purchase.Id,
            CancelUrl = returnUrl + "?status=cancel&pid=" + purchase.Id,
            ClientReferenceId = purchase.Id.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["radiopadFlow"] = "marketplace",
                ["purchaseId"] = purchase.Id.ToString(),
                ["buyerTenantId"] = tenant.Id.ToString(),
            },
        };
        if (!string.IsNullOrEmpty(publisher.StripeConnectAccountId))
        {
            sessionOptions.PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ApplicationFeeAmount = feeAmount,
                Metadata = new Dictionary<string, string>
                {
                    ["radiopadFlow"] = "marketplace",
                    ["purchaseId"] = purchase.Id.ToString(),
                    ["buyerTenantId"] = tenant.Id.ToString(),
                },
                TransferData = new SessionPaymentIntentDataTransferDataOptions
                {
                    Destination = publisher.StripeConnectAccountId,
                },
            };
        }

        var session = await new SessionService().CreateAsync(sessionOptions,
            new RequestOptions { IdempotencyKey = IdemKey(purchase.Id, "checkout", row.StripePriceId ?? "", returnUrl ?? "") },
            cancellationToken: ct);
        purchase.StripeSessionId = session.Id;
        await _db.SaveChangesAsync(ct);
        return Ok(new { url = session.Url, purchaseId = purchase.Id });
    }

    [HttpPost("connect/onboarding")]
    public async Task<IActionResult> ConnectOnboarding([FromQuery] string returnUrl, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin);
        if (forbid is not null) return forbid;
        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured.", kind = "config" });

        if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
        {
            var acct = await new AccountService().CreateAsync(new AccountCreateOptions
            {
                Type = "express",
                Capabilities = new AccountCapabilitiesOptions
                {
                    Transfers = new AccountCapabilitiesTransfersOptions { Requested = true },
                },
            }, new RequestOptions { IdempotencyKey = IdemKey(tenant.Id, "connect-account", tenant.Id.ToString()) }, ct);
            tenant.StripeConnectAccountId = acct.Id;
            await _db.SaveChangesAsync(ct);
        }

        var link = await new AccountLinkService().CreateAsync(new AccountLinkCreateOptions
        {
            Account = tenant.StripeConnectAccountId,
            RefreshUrl = returnUrl,
            ReturnUrl = returnUrl,
            Type = "account_onboarding",
        }, new RequestOptions { IdempotencyKey = IdemKey(tenant.Id, "connect-link", returnUrl ?? "") }, ct);
        return Ok(new { url = link.Url });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var secret = BillingEnv.WebhookSecret;
        Event evt;
        try
        {
            if (string.IsNullOrEmpty(secret))
            {
                if (!_env.IsEnvironment("Testing"))
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable,
                        new { error = "Webhook not configured.", kind = "provider_unavailable" });
                }
                evt = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
            }
            else
            {
                evt = EventUtility.ConstructEvent(payload, Request.Headers["Stripe-Signature"]!, secret, 300, throwOnApiVersionMismatch: false);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Stripe webhook signature mismatch.");
            return BadRequest(new { error = "invalid signature", kind = "validation" });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var dup = await _db.StripeWebhookEvents.AnyAsync(e => e.Source == "marketplace" && e.EventId == evt.Id, ct);
        if (dup) return Ok(new { received = true, deduped = true });
        _db.StripeWebhookEvents.Add(new StripeWebhookEvent { EventId = evt.Id, EventType = evt.Type, Source = "marketplace" });
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Ok(new { received = true, deduped = true }); }

        if (evt.Type == "checkout.session.completed" && evt.Data.Object is Session sess)
        {
            if (sess.Metadata?.TryGetValue("radiopadFlow", out var flow) == true && flow != "marketplace")
            {
                await tx.CommitAsync(ct);
                return Ok(new { received = true });
            }
            if (Guid.TryParse(sess.ClientReferenceId, out var purchaseId))
            {
                var p = await _db.MarketplacePurchases.FirstOrDefaultAsync(x => x.Id == purchaseId, ct);
                if (p is not null && p.Status == "pending")
                {
                    p.Status = "paid";
                    p.PaidAt = DateTimeOffset.UtcNow;
                    p.StripePaymentIntentId = sess.PaymentIntentId;
                    await _db.SaveChangesAsync(ct);
                    await _billingAudit.AppendAsync(p.BuyerTenantId, p.BuyerUserId, "marketplace.purchase.paid",
                        new { purchaseId = p.Id, listingId = p.ListingId, paymentIntentId = sess.PaymentIntentId, amountCents = p.AmountCents }, ct);
                }
            }
        }
        else if (evt.Type == "charge.dispute.created" && evt.Data.Object is Dispute dispute)
        {
            var paymentIntentId = dispute.PaymentIntentId;
            if (!string.IsNullOrEmpty(paymentIntentId))
            {
                var p = await _db.MarketplacePurchases.FirstOrDefaultAsync(x => x.StripePaymentIntentId == paymentIntentId, ct);
                if (p is not null)
                {
                    p.Status = "disputed";
                    await _db.SaveChangesAsync(ct);
                    await _billingAudit.AppendAsync(p.BuyerTenantId, p.BuyerUserId, "marketplace.dispute.opened",
                        new { purchaseId = p.Id, listingId = p.ListingId, paymentIntentId, amountCents = p.AmountCents }, ct);
                }
            }
        }
        await tx.CommitAsync(ct);
        return Ok(new { received = true });
    }

    [HttpGet("connect/status")]
    public async Task<IActionResult> ConnectStatus(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin);
        if (forbid is not null) return forbid;
        if (string.IsNullOrEmpty(BillingEnv.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured.", kind = "config" });

        if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
            return Ok(new { onboarded = false, chargesEnabled = false, payoutsEnabled = false });

        var acct = await new AccountService().GetAsync(tenant.StripeConnectAccountId, cancellationToken: ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(settings);
        }
        var readinessChanged = settings.ChargesEnabled != acct.ChargesEnabled
            || settings.PayoutsEnabled != acct.PayoutsEnabled;
        settings.ChargesEnabled = acct.ChargesEnabled;
        settings.PayoutsEnabled = acct.PayoutsEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (readinessChanged)
        {
            await _billingAudit.AppendAsync(tenant.Id, user.Id, "connect.status",
                new { stripeConnectAccountId = tenant.StripeConnectAccountId, chargesEnabled = acct.ChargesEnabled, payoutsEnabled = acct.PayoutsEnabled }, ct);
        }

        return Ok(new
        {
            onboarded = true,
            chargesEnabled = acct.ChargesEnabled,
            payoutsEnabled = acct.PayoutsEnabled,
            requirements = acct.Requirements?.CurrentlyDue,
        });
    }

    [HttpPost("purchases/{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, [FromBody] MarketplaceRefundDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.BillingAdmin, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (forbid is not null) return forbid;
        if (string.IsNullOrEmpty(BillingEnv.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured.", kind = "config" });

        var purchase = await _db.MarketplacePurchases.FirstOrDefaultAsync(p => p.Id == id && p.BuyerTenantId == tenant.Id, ct);
        if (purchase is null) return NotFound(new { error = "purchase", kind = "not_found" });
        if (purchase.Status != "paid" || string.IsNullOrEmpty(purchase.StripePaymentIntentId))
            return StatusCode(StatusCodes.Status409Conflict, new { error = "Purchase is not refundable.", kind = "validation" });
        if (!string.IsNullOrWhiteSpace(dto.Reason) && !ValidRefundReasons.Contains(dto.Reason))
            return BadRequest(new { error = "reason must be duplicate, fraudulent, or requested_by_customer.", kind = "validation" });

        var refund = await new RefundService().CreateAsync(new RefundCreateOptions
        {
            PaymentIntent = purchase.StripePaymentIntentId,
            Reason = dto.Reason ?? "requested_by_customer",
            ReverseTransfer = true,
            RefundApplicationFee = true,
        }, new RequestOptions { IdempotencyKey = IdemKey(purchase.Id, "refund") }, ct);

        purchase.Status = "refunded";
        await _db.SaveChangesAsync(ct);
        await _billingAudit.AppendAsync(tenant.Id, user.Id, "marketplace.refund",
            new { purchaseId = purchase.Id, listingId = purchase.ListingId, paymentIntentId = purchase.StripePaymentIntentId, amountCents = purchase.AmountCents, refundId = refund.Id }, ct);
        return Ok(new { id = refund.Id, status = refund.Status, amount = refund.Amount });
    }

    // ─── PRD Enterprise GA #13: Marketplace Submission & Approval Workflow ───

    /// <summary>
    /// Submit a rulebook or template for marketplace review. Copies the
    /// current content into a new MarketplaceListing with status PendingReview.
    /// </summary>
    [HttpPost("submissions")]
    public async Task<IActionResult> SubmitForReview([FromBody] SubmissionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(dto.Category) || string.IsNullOrWhiteSpace(dto.SourceId))
            return BadRequest(new { error = "category and sourceId are required.", kind = "validation" });

        string artifactBody;
        string name;
        string? sourceRulebookId = null;
        string? sourceTemplateId = null;

        if (dto.Category == "rulebook")
        {
            var rb = await _db.Rulebooks.FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.RulebookId == dto.SourceId, ct);
            if (rb is null) return NotFound(new { error = "source rulebook not found.", kind = "not_found" });
            artifactBody = rb.SourceYaml;
            name = rb.Name;
            sourceRulebookId = rb.RulebookId;
        }
        else if (dto.Category == "template")
        {
            var tmpl = await _db.Templates.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.TemplateId == dto.SourceId, ct);
            if (tmpl is null) return NotFound(new { error = "source template not found.", kind = "not_found" });
            artifactBody = tmpl.SectionsJson;
            name = tmpl.Name;
            sourceTemplateId = tmpl.TemplateId;
        }
        else if (dto.Category == "prompt_pack")
        {
            artifactBody = dto.SourceId; // prompt packs: sourceId is treated as the content identifier
            name = $"Prompt Pack {dto.SourceId}";
        }
        else
        {
            return BadRequest(new { error = "category must be rulebook, template, or prompt_pack.", kind = "validation" });
        }

        var listing = new MarketplaceListing
        {
            PublisherTenantId = tenant.Id,
            PublisherUserId = user.Id,
            Name = name,
            Description = dto.Description ?? "",
            Kind = dto.Category,
            ArtifactBody = artifactBody,
            PriceCents = 0,
            Status = "pending_review",
            SubmittedAt = DateTimeOffset.UtcNow,
            SourceRulebookId = sourceRulebookId,
            SourceTemplateId = sourceTemplateId,
            Version = string.IsNullOrWhiteSpace(dto.Version) ? "1.0.0" : dto.Version,
        };
        _db.MarketplaceListings.Add(listing);
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MarketplaceSubmission,
            DetailsJson = JsonSerializer.Serialize(new { listingId = listing.Id, category = dto.Category, sourceId = dto.SourceId, version = listing.Version }),
        }, ct);

        return Ok(new { listing.Id, listing.Status });
    }

    /// <summary>
    /// List submissions. Reviewers (Admin/MedicalDirector) see all pending
    /// submissions; regular users see only their own.
    /// </summary>
    [HttpGet("submissions")]
    public async Task<IActionResult> ListSubmissions(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var isReviewer = user.Role == UserRole.MedicalDirector || user.Role == UserRole.ItAdmin;

        IQueryable<MarketplaceListing> q;
        if (isReviewer)
        {
            // Reviewers see all pending submissions + their own
            q = _db.MarketplaceListings.Where(l =>
                l.Status == "pending_review" ||
                l.PublisherTenantId == tenant.Id);
        }
        else
        {
            q = _db.MarketplaceListings.Where(l => l.PublisherTenantId == tenant.Id && l.PublisherUserId == user.Id);
        }

        var list = await q
            .OrderByDescending(l => l.SubmittedAt ?? l.CreatedAt)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Description,
                l.Kind,
                l.Status,
                l.Version,
                l.InstallCount,
                l.SubmittedAt,
                l.ReviewedAt,
                l.ReviewNotes,
                l.RejectionReason,
                publisher = l.PublisherTenantId,
                publisherUser = l.PublisherUserId,
            })
            .ToListAsync(ct);

        return Ok(list);
    }

    /// <summary>
    /// Approve a marketplace submission. Admin/MedicalDirector only.
    /// Sets status to approved and makes the listing visible in the catalogue.
    /// </summary>
    [HttpPost("submissions/{id:guid}/approve")]
    public async Task<IActionResult> ApproveSubmission(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (forbid is not null) return forbid;

        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null) return NotFound(new { error = "submission", kind = "not_found" });
        if (row.Status != "pending_review")
            return BadRequest(new { error = "Only pending_review submissions can be approved.", kind = "validation" });

        row.Status = "approved";
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewerUserId = user.Id;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MarketplaceApproved,
            DetailsJson = JsonSerializer.Serialize(new { listingId = row.Id, kind = row.Kind }),
        }, ct);

        return Ok(new { row.Status });
    }

    /// <summary>
    /// Reject a marketplace submission. Admin/MedicalDirector only.
    /// Sets status to rejected with reviewer notes.
    /// </summary>
    [HttpPost("submissions/{id:guid}/reject")]
    public async Task<IActionResult> RejectSubmission(Guid id, [FromBody] SubmissionRejectDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (forbid is not null) return forbid;

        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null) return NotFound(new { error = "submission", kind = "not_found" });
        if (row.Status != "pending_review")
            return BadRequest(new { error = "Only pending_review submissions can be rejected.", kind = "validation" });

        row.Status = "rejected";
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewerUserId = user.Id;
        row.ReviewNotes = dto.ReviewNotes;
        row.RejectionReason = dto.ReviewNotes;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MarketplaceRejected,
            DetailsJson = JsonSerializer.Serialize(new { listingId = row.Id, kind = row.Kind, reviewNotes = dto.ReviewNotes }),
        }, ct);

        return Ok(new { row.Status, row.ReviewNotes });
    }

    /// <summary>
    /// Install an approved marketplace listing into the current tenant.
    /// Copies the content as a new rulebook/template in Draft status.
    /// Increments the listing's InstallCount.
    /// </summary>
    [HttpPost("listings/{id:guid}/install")]
    public async Task<IActionResult> Install(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.Id == id && l.Status == "approved", ct);
        if (row is null) return NotFound(new { error = "listing", kind = "not_found" });

        Guid? installedId = null;

        if (row.Kind == "rulebook")
        {
            var rulebookId = row.SourceRulebookId ?? $"mp-{row.Id:N}";
            var existing = await _db.Rulebooks.FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.RulebookId == rulebookId && r.Version == row.Version, ct);
            if (existing is not null)
                return BadRequest(new { error = "This rulebook version is already installed.", kind = "duplicate" });

            var rb = new Rulebook
            {
                TenantId = tenant.Id,
                RulebookId = rulebookId,
                Name = $"[Marketplace] {row.Name}",
                Version = row.Version,
                Owner = user.Email,
                Status = RulebookStatus.Draft,
                SourceYaml = row.ArtifactBody,
            };
            _db.Rulebooks.Add(rb);
            installedId = rb.Id;
        }
        else if (row.Kind == "template")
        {
            var templateId = row.SourceTemplateId ?? $"mp-{row.Id:N}";
            var existing = await _db.Templates.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.TemplateId == templateId, ct);
            if (existing is not null)
                return BadRequest(new { error = "This template is already installed.", kind = "duplicate" });

            var tmpl = new ReportTemplate
            {
                TenantId = tenant.Id,
                TemplateId = templateId,
                Name = $"[Marketplace] {row.Name}",
                SectionsJson = row.ArtifactBody,
                Status = TemplateStatus.Draft,
            };
            _db.Templates.Add(tmpl);
            installedId = tmpl.Id;
        }
        else
        {
            // prompt_pack or other: no local entity to create, just record the install
        }

        row.InstallCount++;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MarketplaceInstalled,
            DetailsJson = JsonSerializer.Serialize(new { listingId = row.Id, kind = row.Kind, installedId, installCount = row.InstallCount }),
        }, ct);

        return Ok(new { installed = true, installedId, installCount = row.InstallCount });
    }
}
