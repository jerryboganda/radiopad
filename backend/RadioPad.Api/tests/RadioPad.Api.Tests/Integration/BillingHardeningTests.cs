using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PRD BILL-001..006 — billing hardening (Agent 6 of 8). Covers BillingEnv
/// precedence, subscription-lifecycle mapping, billing-audit hashing, plan
/// quota enforcement, suspension guard, and the new BillingController and
/// MarketplaceController endpoints (invoices/refund/status, connect/status,
/// purchases/{id}/refund) plus webhook idempotency.
/// </summary>
[Collection(RadioPad.Api.Tests.Billing.StripeWebhookEnvCollection.Name)]
public class BillingHardeningTests : IClassFixture<RadioPadAppFactory>
{
    private const string TestWebhookSecret = "whsec_radiopad_billing_hardening_test";
    private readonly RadioPadAppFactory _factory;

    public BillingHardeningTests(RadioPadAppFactory f) => _factory = f;

    // --------------------------------------------------------------------
    // 1-3: BillingEnv precedence
    // --------------------------------------------------------------------

    [Fact]
    public void BillingEnv_PrefersCanonicalOverLegacy()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", "A");
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "B");
            Assert.Equal("A", BillingEnv.SecretKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", prevLegacy);
        }
    }

    [Fact]
    public void BillingEnv_FallsBackToLegacy()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", null);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "B");
            Assert.Equal("B", BillingEnv.SecretKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", prevLegacy);
        }
    }

    [Fact]
    public void BillingEnv_NullWhenUnset()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", null);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", null);
            Assert.Null(BillingEnv.SecretKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", prevLegacy);
        }
    }

    // --------------------------------------------------------------------
    // 4-6: SubscriptionLifecycleService.Apply
    // --------------------------------------------------------------------

    [Fact]
    public void SubscriptionLifecycle_PastDue_SetsGracePeriod_When_NotAlreadySet()
    {
        var svc = new SubscriptionLifecycleService();
        var s = new TenantSettings { TenantId = Guid.NewGuid() };
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        svc.Apply(s, "past_due", trialEnd: null, now);

        Assert.NotNull(s.GracePeriodUntil);
        Assert.True(s.GracePeriodUntil > now);
        Assert.Null(s.SuspendedAt);
        Assert.Equal("past_due", s.StripeSubscriptionStatus);
    }

    [Fact]
    public void SubscriptionLifecycle_Active_ClearsGraceAndSuspended()
    {
        var svc = new SubscriptionLifecycleService();
        var now = DateTimeOffset.UtcNow;
        var s = new TenantSettings
        {
            TenantId = Guid.NewGuid(),
            GracePeriodUntil = now.AddDays(2),
            SuspendedAt = now.AddDays(-1),
            TrialEndsAt = now.AddDays(5),
        };

        svc.Apply(s, "active", trialEnd: null, now);

        Assert.Null(s.GracePeriodUntil);
        Assert.Null(s.SuspendedAt);
        Assert.Null(s.TrialEndsAt);
        Assert.Equal("active", s.StripeSubscriptionStatus);
    }

    [Fact]
    public void SubscriptionLifecycle_Canceled_SetsSuspendedAt()
    {
        var svc = new SubscriptionLifecycleService();
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var s = new TenantSettings { TenantId = Guid.NewGuid() };

        svc.Apply(s, "canceled", trialEnd: null, now);

        Assert.Equal(now, s.SuspendedAt);
        Assert.Null(s.GracePeriodUntil);
        Assert.Equal("canceled", s.StripeSubscriptionStatus);
    }

    // --------------------------------------------------------------------
    // 7: BillingAudit hashes sensitive identifiers
    // --------------------------------------------------------------------

    [Fact]
    public async Task BillingAudit_HashesEmailAndCustomerId()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var billingAudit = sp.GetRequiredService<IBillingAudit>();
        var db = sp.GetRequiredService<RadioPadDbContext>();
        var tenantId = _factory.SeedTenant.Id;

        var before = await db.AuditEvents
            .Where(a => a.TenantId == tenantId && a.Action == AuditAction.BillingChanged)
            .Select(a => a.Id)
            .ToListAsync();

        await billingAudit.AppendAsync(tenantId, _factory.SeedUser.Id, "test.hashing", new
        {
            email = "x@y.com",
            stripeCustomerId = "cus_123",
            paymentIntentId = "pi_abc",
            note = "kept-as-is",
        }, default);

        var added = await db.AuditEvents
            .Where(a => a.TenantId == tenantId
                && a.Action == AuditAction.BillingChanged
                && !before.Contains(a.Id))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(added);
        var json = added!.DetailsJson;
        Assert.DoesNotContain("x@y.com", json);
        Assert.DoesNotContain("cus_123", json);
        Assert.DoesNotContain("pi_abc", json);
        Assert.Contains("sha16:", json);
        Assert.Contains("kept-as-is", json);
        Assert.Contains("test.hashing", json);
    }

    // --------------------------------------------------------------------
    // 8-10: PlanQuotaService
    // --------------------------------------------------------------------

    [Fact]
    public async Task PlanQuota_TrialPlan_OverLimit_DeniesWithReasonAiCalls()
    {
        // Use a fresh tenant id so we don't pollute the seeded tenant's
        // AiRequest history (which other tests in this class may inspect).
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var fakeTenant = new Tenant { Id = tenantId, Slug = $"quota-{tenantId:N}", DisplayName = "Quota" };
        db.Tenants.Add(fakeTenant);
        var settings = new TenantSettings { TenantId = tenantId, Plan = TenantPlan.Trial };
        db.TenantSettings.Add(settings);

        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 100; i++)
        {
            db.AiRequests.Add(new AiRequest
            {
                TenantId = tenantId,
                UserId = Guid.Empty,
                Provider = "mock",
                Model = "mock",
                Mode = "draft",
                Status = "ok",
                CreatedAt = monthStart.AddMinutes(i),
                InputHash = "x",
                OutputHash = "y",
            });
        }
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<PlanQuotaService>();
        var result = await svc.CheckAsync(fakeTenant, settings, default);

        Assert.False(result.AllowedToProceed);
        Assert.Equal("ai_calls", result.Reason);
        Assert.Equal(100, result.AiCallsUsed);
        Assert.Equal(TenantPlan.Trial, result.Plan);
    }

    [Fact]
    public async Task PlanQuota_SuspendedTenant_DeniesWithReasonSuspended()
    {
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var fakeTenant = new Tenant { Id = tenantId, Slug = $"susp-{tenantId:N}", DisplayName = "Susp" };
        db.Tenants.Add(fakeTenant);
        var settings = new TenantSettings
        {
            TenantId = tenantId,
            Plan = TenantPlan.Team,
            SuspendedAt = DateTimeOffset.UtcNow,
        };
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<PlanQuotaService>();
        var result = await svc.CheckAsync(fakeTenant, settings, default);

        Assert.False(result.AllowedToProceed);
        Assert.Equal("suspended", result.Reason);
    }

    [Fact]
    public async Task PlanQuota_Enterprise_AllowsLargeUsage()
    {
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var fakeTenant = new Tenant { Id = tenantId, Slug = $"ent-{tenantId:N}", DisplayName = "Ent" };
        db.Tenants.Add(fakeTenant);
        var settings = new TenantSettings { TenantId = tenantId, Plan = TenantPlan.Enterprise };
        db.TenantSettings.Add(settings);

        // 50_000 AI calls — far above the Trial/Team caps but well under int.MaxValue.
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var batch = new List<AiRequest>(50_000);
        for (var i = 0; i < 50_000; i++)
        {
            batch.Add(new AiRequest
            {
                TenantId = tenantId,
                UserId = Guid.Empty,
                Provider = "mock",
                Model = "mock",
                Mode = "draft",
                Status = "ok",
                CreatedAt = monthStart,
                InputHash = "x",
                OutputHash = "y",
            });
        }
        db.AiRequests.AddRange(batch);
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<PlanQuotaService>();
        var result = await svc.CheckAsync(fakeTenant, settings, default);

        Assert.True(result.AllowedToProceed);
        Assert.Equal(TenantPlan.Enterprise, result.Plan);
        Assert.Equal(50_000, result.AiCallsUsed);
    }

    // --------------------------------------------------------------------
    // 11-12: Webhook idempotency / signature
    // --------------------------------------------------------------------

    [Fact]
    public async Task Webhook_Idempotent_DuplicateEventReturnsDeduped()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", TestWebhookSecret);
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        try
        {
            var eventId = $"evt_dedupe_{Guid.NewGuid():N}";
            var payload = $"{{\"id\":\"{eventId}\",\"object\":\"event\",\"type\":\"ping\",\"request\":{{\"id\":\"req_test\",\"idempotency_key\":null}},\"data\":{{\"object\":{{}}}}}}";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signed = $"{ts}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestWebhookSecret));
            var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
            var sigHeader = $"t={ts},v1={sig}";

            using var client = _factory.CreateClient();

            var first = await SendWebhookAsync(client, payload, sigHeader);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstDoc = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
            // First delivery — deduped flag is either absent or false.
            var firstDeduped = firstDoc.RootElement.TryGetProperty("deduped", out var d1)
                && d1.ValueKind == JsonValueKind.True;
            Assert.False(firstDeduped);

            var second = await SendWebhookAsync(client, payload, sigHeader);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var secondDoc = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
            Assert.True(secondDoc.RootElement.TryGetProperty("deduped", out var d2) && d2.GetBoolean());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rows = await db.StripeWebhookEvents.Where(e => e.EventId == eventId).CountAsync();
            Assert.Equal(1, rows);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", prevLegacy);
        }
    }

    [Fact]
    public async Task Webhook_BadSignature_Returns400()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", TestWebhookSecret);
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        try
        {
            using var client = _factory.CreateClient();
            var resp = await SendWebhookAsync(client, "{\"id\":\"evt_bad\"}", "t=1234,v1=deadbeef");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", prevLegacy);
        }
    }

    private static Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string payload, string signature)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Stripe-Signature", signature);
        return client.SendAsync(req);
    }

    // --------------------------------------------------------------------
    // 13: GET /api/billing/status shape
    // --------------------------------------------------------------------

    [Fact]
    public async Task BillingStatus_ReturnsPlanAndSubscriptionShape()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/billing/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("plan", out _), "plan key missing");
        Assert.True(root.TryGetProperty("subscriptionStatus", out _), "subscriptionStatus key missing");
        Assert.True(root.TryGetProperty("customerConfigured", out _), "customerConfigured key missing");
    }

    // --------------------------------------------------------------------
    // 14-15: POST /api/billing/refund RBAC + 503
    // --------------------------------------------------------------------

    [Fact]
    public async Task Refund_RequiresBillingAdminRole()
    {
        // Seeded user is Radiologist — should be denied.
        var prev = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        try
        {
            // Set a fake secret so the 503-not-configured branch isn't what
            // produces the rejection — we want to verify the RBAC gate.
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", "sk_test_fake");
            Stripe.StripeConfiguration.ApiKey = "sk_test_fake";

            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/billing/refund", new
            {
                paymentIntentId = "pi_test_xyz",
                amountCents = (long?)null,
                reason = "requested_by_customer",
            });
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prev);
        }
    }

    [Fact]
    public async Task Refund_503_WhenStripeUnconfigured()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        var prevLegacy = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        var prevApiKey = Stripe.StripeConfiguration.ApiKey;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var originalRole = user.Role;
        user.Role = UserRole.BillingAdmin;
        await db.SaveChangesAsync();

        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", null);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", null);
            Stripe.StripeConfiguration.ApiKey = null;

            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/billing/refund", new
            {
                paymentIntentId = "pi_test_xyz",
                amountCents = (long?)null,
                reason = "requested_by_customer",
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("provider_unavailable", doc.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            user.Role = originalRole;
            await db.SaveChangesAsync();
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", prevLegacy);
            Stripe.StripeConfiguration.ApiKey = prevApiKey;
        }
    }

    // --------------------------------------------------------------------
    // 16: Marketplace checkout blocked when publisher charges not enabled
    // --------------------------------------------------------------------

    [Fact]
    public async Task Marketplace_Checkout_Blocked_WhenPublisherChargesNotEnabled()
    {
        var prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY");
        var prevApiKey = Stripe.StripeConfiguration.ApiKey;

        Guid listingId, publisherTenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var publisher = new Tenant
            {
                Slug = $"pub-{Guid.NewGuid():N}",
                DisplayName = "Publisher",
                StripeConnectAccountId = "acct_x",
            };
            db.Tenants.Add(publisher);
            var pubSettings = new TenantSettings
            {
                TenantId = publisher.Id,
                ChargesEnabled = false,
                PayoutsEnabled = false,
            };
            db.TenantSettings.Add(pubSettings);
            var listing = new MarketplaceListing
            {
                PublisherTenantId = publisher.Id,
                PublisherUserId = Guid.NewGuid(),
                Name = "Blocked listing",
                Description = "test",
                Kind = "rulebook",
                ArtifactBody = "rulebook_id: x\nversion: 0.0.1",
                PriceCents = 4900,
                Status = "approved",
                ReviewedAt = DateTimeOffset.UtcNow,
                StripePriceId = "price_test_blocked",
            };
            db.MarketplaceListings.Add(listing);
            await db.SaveChangesAsync();
            listingId = listing.Id;
            publisherTenantId = publisher.Id;
        }

        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", "sk_test_fake");
            Stripe.StripeConfiguration.ApiKey = "sk_test_fake";

            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsync(
                $"/api/marketplace/listings/{listingId}/checkout?returnUrl=https://example.com/r",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("connect_not_ready", doc.RootElement.GetProperty("kind").GetString());

            // Audit row should record the checkout block via IBillingAudit
            // (BillingChanged action). The detail JSON carries the listing id.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var any = await db.AuditEvents
                .Where(a => a.TenantId == _factory.SeedTenant.Id
                    && a.Action == AuditAction.BillingChanged
                    && a.DetailsJson.Contains("connect_not_ready"))
                .AnyAsync();
            Assert.True(any, "Expected a BillingChanged audit row recording the connect_not_ready block.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_SECRET_KEY", prevCanonical);
            Stripe.StripeConfiguration.ApiKey = prevApiKey;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var listing = await db.MarketplaceListings.FindAsync(listingId);
            if (listing is not null) db.MarketplaceListings.Remove(listing);
            var pubSettings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == publisherTenantId);
            if (pubSettings is not null) db.TenantSettings.Remove(pubSettings);
            var pubTenant = await db.Tenants.FindAsync(publisherTenantId);
            if (pubTenant is not null) db.Tenants.Remove(pubTenant);
            // Clean any pending purchase rows the controller created.
            var purchases = await db.MarketplacePurchases.Where(p => p.ListingId == listingId).ToListAsync();
            db.MarketplacePurchases.RemoveRange(purchases);
            await db.SaveChangesAsync();
        }
    }

    // --------------------------------------------------------------------
    // 17-18: SuspensionGuardMiddleware
    // --------------------------------------------------------------------

    [Fact]
    public async Task SuspensionGuard_Blocks_MutatingEndpoint_With402()
    {
        await SetSuspendedAsync(_factory.SeedTenant.Id, DateTimeOffset.UtcNow);
        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "x",
                accessionNumber = $"ACC-SUSP-{Guid.NewGuid():N}",
            });
            Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("tenant_suspended", doc.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            await SetSuspendedAsync(_factory.SeedTenant.Id, null);
        }
    }

    [Fact]
    public async Task SuspensionGuard_Allows_BillingEndpoints()
    {
        await SetSuspendedAsync(_factory.SeedTenant.Id, DateTimeOffset.UtcNow);
        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.GetAsync("/api/billing/status");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            await SetSuspendedAsync(_factory.SeedTenant.Id, null);
        }
    }

    private async Task SetSuspendedAsync(Guid tenantId, DateTimeOffset? suspendedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        if (s is null)
        {
            s = new TenantSettings { TenantId = tenantId };
            db.TenantSettings.Add(s);
        }
        s.SuspendedAt = suspendedAt;
        await db.SaveChangesAsync();
    }

    // --------------------------------------------------------------------
    // 19: Quota exceeded → 402 RFC-7807 + PolicyViolation audit
    // --------------------------------------------------------------------

    [Fact]
    public async Task Quota_Exceeded_ReturnsRfc7807_402()
    {
        var tenantId = _factory.SeedTenant.Id;
        // Seed Plan=Trial + 100 ok AI calls in the current month.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
            if (s is null)
            {
                s = new TenantSettings { TenantId = tenantId };
                db.TenantSettings.Add(s);
            }
            s.Plan = TenantPlan.Trial;
            s.SuspendedAt = null;
            s.GracePeriodUntil = null;

            var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 100; i++)
            {
                db.AiRequests.Add(new AiRequest
                {
                    TenantId = tenantId,
                    UserId = _factory.SeedUser.Id,
                    Provider = "mock",
                    Model = "mock",
                    Mode = "draft",
                    Status = "ok",
                    CreatedAt = monthStart.AddSeconds(i),
                    InputHash = "h",
                    OutputHash = "h",
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _factory.CreateTenantClient();
            var make = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "Quota test",
                accessionNumber = $"ACC-QUOTA-{Guid.NewGuid():N}",
            });
            Assert.Equal(HttpStatusCode.Created, make.StatusCode);
            var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
                .RootElement.GetProperty("id").GetGuid();
            await client.PatchAsJsonAsync($"/api/reports/{id}", new
            {
                findings = "Lungs clear.",
                impression = "No acute findings.",
            });

            var resp = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
            {
                mode = "impression",
                providerId = _factory.MockProvider.Id,
            });
            Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
            Assert.Equal("application/problem+json",
                resp.Content.Headers.ContentType?.MediaType);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("quota_exceeded", doc.RootElement.GetProperty("kind").GetString());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var any = await db.AuditEvents
                .Where(a => a.TenantId == tenantId
                    && a.Action == AuditAction.PolicyViolation
                    && a.DetailsJson.Contains("quota_exceeded"))
                .AnyAsync();
            Assert.True(any, "Expected a PolicyViolation audit row recording the quota breach.");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
            if (s is not null) { db.TenantSettings.Remove(s); }
            var requests = await db.AiRequests
                .Where(r => r.TenantId == tenantId && r.InputHash == "h")
                .ToListAsync();
            db.AiRequests.RemoveRange(requests);
            await db.SaveChangesAsync();
        }
    }

    // --------------------------------------------------------------------
    // 20: BillingAudit appends a BillingChanged audit row
    // --------------------------------------------------------------------

    [Fact]
    public async Task BillingAudit_AppendsBillingChangedRow()
    {
        // We exercise the helper directly via DI rather than calling
        // /api/billing/checkout because the latter requires a live Stripe
        // network call; the audit-row contract is the same either way.
        using var scope = _factory.Services.CreateScope();
        var billingAudit = scope.ServiceProvider.GetRequiredService<IBillingAudit>();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tenantId = _factory.SeedTenant.Id;

        var beforeCount = await db.AuditEvents
            .Where(a => a.TenantId == tenantId && a.Action == AuditAction.BillingChanged)
            .CountAsync();

        await billingAudit.AppendAsync(tenantId, _factory.SeedUser.Id, "checkout.start", new
        {
            stripeCustomerId = "cus_audit_test",
            priceId = "price_test_audit",
        }, default);

        var afterCount = await db.AuditEvents
            .Where(a => a.TenantId == tenantId && a.Action == AuditAction.BillingChanged)
            .CountAsync();

        Assert.True(afterCount >= beforeCount + 1,
            $"Expected at least one new BillingChanged row (before={beforeCount}, after={afterCount}).");
    }
}
