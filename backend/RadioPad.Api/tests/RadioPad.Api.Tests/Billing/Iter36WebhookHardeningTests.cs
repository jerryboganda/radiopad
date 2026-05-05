using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Tests.Integration;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Billing;

/// <summary>
/// Iter-36 — Stripe webhook hardening for invoice.payment_succeeded /
/// invoice.payment_failed. Drives <see cref="TenantSettings.GracePeriodUntil"/>
/// and <see cref="TenantSettings.SuspendedAt"/> through the existing
/// <c>SubscriptionLifecycleService</c>. Tenant is resolved by Stripe customer
/// id; events dedupe through <c>StripeWebhookEvents</c>.
/// </summary>
[Collection(StripeWebhookEnvCollection.Name)]
public class Iter36WebhookHardeningTests : IClassFixture<RadioPadAppFactory>
{
    private const string TestWebhookSecret = "whsec_radiopad_iter36_test_secret";
    private readonly RadioPadAppFactory _factory;

    public Iter36WebhookHardeningTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task PaymentSucceeded_ClearsGraceAndSuspension()
    {
        await using var env = await WebhookEnvAsync();
        var customerId = $"cus_iter36_{Guid.NewGuid():N}";
        await SeedSettingsAsync(customerId, s =>
        {
            s.GracePeriodUntil = DateTimeOffset.UtcNow.AddDays(3);
            s.SuspendedAt = DateTimeOffset.UtcNow.AddDays(-1);
            s.StripeSubscriptionStatus = "past_due";
        });

        var payload = InvoicePayload($"evt_paid_{Guid.NewGuid():N}",
            "invoice.payment_succeeded", customerId);
        var resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var settings = await ReadSettingsAsync(customerId);
        Assert.NotNull(settings);
        Assert.Null(settings!.GracePeriodUntil);
        Assert.Null(settings.SuspendedAt);
        Assert.Equal("active", settings.StripeSubscriptionStatus);
    }

    [Fact]
    public async Task PaymentFailed_FirstTime_OpensSevenDayGrace()
    {
        await using var env = await WebhookEnvAsync();
        var customerId = $"cus_iter36_{Guid.NewGuid():N}";
        await SeedSettingsAsync(customerId, s => { });

        var before = DateTimeOffset.UtcNow;
        var payload = InvoicePayload($"evt_fail_{Guid.NewGuid():N}",
            "invoice.payment_failed", customerId);
        var resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var after = DateTimeOffset.UtcNow;

        var settings = await ReadSettingsAsync(customerId);
        Assert.NotNull(settings);
        Assert.NotNull(settings!.GracePeriodUntil);
        var grace = settings.GracePeriodUntil!.Value;
        Assert.True(grace >= before.AddDays(7).AddSeconds(-5));
        Assert.True(grace <= after.AddDays(7).AddSeconds(5));
        Assert.Null(settings.SuspendedAt);
        Assert.Equal("past_due", settings.StripeSubscriptionStatus);
    }

    [Fact]
    public async Task PaymentFailed_AfterGraceExpired_SetsSuspendedAt()
    {
        await using var env = await WebhookEnvAsync();
        var customerId = $"cus_iter36_{Guid.NewGuid():N}";
        var expired = DateTimeOffset.UtcNow.AddDays(-1);
        await SeedSettingsAsync(customerId, s =>
        {
            s.GracePeriodUntil = expired;
            s.StripeSubscriptionStatus = "past_due";
        });

        var payload = InvoicePayload($"evt_fail2_{Guid.NewGuid():N}",
            "invoice.payment_failed", customerId);
        var resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var settings = await ReadSettingsAsync(customerId);
        Assert.NotNull(settings);
        Assert.NotNull(settings!.SuspendedAt);
        Assert.Equal("past_due", settings.StripeSubscriptionStatus);
    }

    [Fact]
    public async Task DuplicateEventId_IsNoOp()
    {
        await using var env = await WebhookEnvAsync();
        var customerId = $"cus_iter36_{Guid.NewGuid():N}";
        await SeedSettingsAsync(customerId, s => { });

        var eventId = $"evt_dup_{Guid.NewGuid():N}";
        var payload = InvoicePayload(eventId, "invoice.payment_failed", customerId);

        var first = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Mutate the second delivery: clear the grace window so we can detect
        // whether the dedupe path was taken (a re-run would re-open it).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstAsync(x => x.StripeCustomerId == customerId);
            s.GracePeriodUntil = null;
            s.StripeSubscriptionStatus = "active";
            await db.SaveChangesAsync();
        }

        var second = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var doc = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("deduped", out var deduped) && deduped.GetBoolean());

        var settings = await ReadSettingsAsync(customerId);
        Assert.NotNull(settings);
        // No re-handling: GracePeriodUntil should still be null and status untouched.
        Assert.Null(settings!.GracePeriodUntil);
        Assert.Equal("active", settings.StripeSubscriptionStatus);
    }

    [Fact]
    public async Task PaymentFailed_DuplicateCustomerId_DoesNotMutateAnyTenant()
    {
        await using var env = await WebhookEnvAsync();
        var customerId = $"cus_iter36_{Guid.NewGuid():N}";
        await SeedSettingsAsync(customerId, s => s.StripeSubscriptionStatus = "active");
        await SeedDuplicateTenantSettingsAsync(customerId, s => s.StripeSubscriptionStatus = "active");

        var payload = InvoicePayload($"evt_dup_customer_{Guid.NewGuid():N}",
            "invoice.payment_failed", customerId);
        var resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var settings = await ReadAllSettingsAsync(customerId);
        Assert.Equal(2, settings.Count);
        Assert.All(settings, s =>
        {
            Assert.Equal("active", s.StripeSubscriptionStatus);
            Assert.Null(s.GracePeriodUntil);
            Assert.Null(s.SuspendedAt);
        });
    }

    [Fact]
    public async Task SignatureMismatch_Returns400()
    {
        await using var env = await WebhookEnvAsync();
        using var client = _factory.CreateClient();
        var payload = InvoicePayload("evt_badsig", "invoice.payment_failed", "cus_x");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Stripe-Signature", "t=1234,v1=deadbeef");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // -------------------------- helpers --------------------------

    private async Task<HttpResponseMessage> PostWebhookAsync(string payload)
    {
        using var client = _factory.CreateClient();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = $"{ts}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestWebhookSecret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Stripe-Signature", $"t={ts},v1={sig}");
        return await client.SendAsync(req);
    }

    private static string InvoicePayload(string eventId, string type, string customerId)
    {
        var invoiceId = $"in_{Guid.NewGuid():N}";
        return "{" +
            $"\"id\":\"{eventId}\",\"object\":\"event\",\"type\":\"{type}\"," +
            "\"request\":{\"id\":\"req_test\",\"idempotency_key\":null}," +
            "\"data\":{\"object\":{" +
                $"\"id\":\"{invoiceId}\",\"object\":\"invoice\"," +
                $"\"customer\":\"{customerId}\",\"subscription\":\"sub_test\"," +
                "\"amount_due\":1999,\"amount_paid\":1999,\"currency\":\"usd\"" +
            "}}}";
    }

    private async Task SeedSettingsAsync(string customerId, Action<TenantSettings> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        // Remove any existing settings row for the seeded tenant — different
        // tests share the same tenant but each owns its own customer id.
        var existing = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _factory.SeedTenant.Id);
        if (existing is not null) db.TenantSettings.Remove(existing);
        await db.SaveChangesAsync();

        var s = new TenantSettings
        {
            TenantId = _factory.SeedTenant.Id,
            Plan = TenantPlan.Team,
            StripeCustomerId = customerId,
            StripeSubscriptionId = "sub_test",
        };
        mutate(s);
        db.TenantSettings.Add(s);
        await db.SaveChangesAsync();
    }

    private async Task SeedDuplicateTenantSettingsAsync(string customerId, Action<TenantSettings> mutate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tenant = new Tenant
        {
            Slug = $"it-dup-{Guid.NewGuid():N}",
            DisplayName = "Integration duplicate customer tenant",
        };
        var s = new TenantSettings
        {
            TenantId = tenant.Id,
            Plan = TenantPlan.Team,
            StripeCustomerId = customerId,
            StripeSubscriptionId = "sub_test_duplicate",
        };
        mutate(s);
        db.Tenants.Add(tenant);
        db.TenantSettings.Add(s);
        await db.SaveChangesAsync();
    }

    private async Task<TenantSettings?> ReadSettingsAsync(string customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        return await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StripeCustomerId == customerId);
    }

    private async Task<List<TenantSettings>> ReadAllSettingsAsync(string customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        return await db.TenantSettings.AsNoTracking()
            .Where(s => s.StripeCustomerId == customerId)
            .OrderBy(s => s.TenantId)
            .ToListAsync();
    }

    private static Task<WebhookEnv> WebhookEnvAsync()
        => Task.FromResult(new WebhookEnv(TestWebhookSecret));

    private sealed class WebhookEnv : IAsyncDisposable
    {
        private readonly string? _prevCanonical;
        private readonly string? _prevLegacy;

        public WebhookEnv(string secret)
        {
            _prevCanonical = Environment.GetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET");
            _prevLegacy = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", secret);
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("RADIOPAD_STRIPE_WEBHOOK_SECRET", _prevCanonical);
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", _prevLegacy);
            return ValueTask.CompletedTask;
        }
    }
}
