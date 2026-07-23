using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// NOTIF-001 contract tests for the own-inbox endpoints (list/filters/paging,
/// unread-count, read idempotency, ack, NOTIF-011 bulk confirmation, prefs +
/// mandatory-category guard, tenant/user isolation). Runs against the full
/// middleware + controller pipeline via <see cref="RadioPadAppFactory"/> with dev
/// headers (the seed radiologist).
/// </summary>
public class NotificationsEndpointsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public NotificationsEndpointsTests(RadioPadAppFactory factory) => _factory = factory;

    private async Task<Guid> SeedAsync(Action<Notification> configure, Guid? userId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var n = new Notification
        {
            TenantId = _factory.SeedTenant.Id,
            UserId = userId ?? _factory.SeedUser.Id,
            Category = NotificationCategory.PeerReview,
            Urgency = NotificationUrgency.Info,
            Title = "Workflow event",
            Body = "",
        };
        configure(n);
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    private async Task<int> AuditCountAsync(AuditAction action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        return await db.AuditEvents.CountAsync(e => e.TenantId == _factory.SeedTenant.Id && e.Action == action);
    }

    [Fact]
    public async Task List_PagesByCursor_AndOmitsNullFields_InCamelCase()
    {
        // Scoped to RulebookApproval — a category no other test in this shared-fixture class
        // seeds — so the array lengths are deterministic regardless of test ordering.
        const NotificationCategory scope = NotificationCategory.RulebookApproval;
        var baseTime = DateTimeOffset.UtcNow;
        await SeedAsync(n => { n.Category = scope; n.CreatedAt = baseTime; n.Title = "newest"; n.LinkHref = null; });
        await SeedAsync(n => { n.Category = scope; n.CreatedAt = baseTime.AddMinutes(-1); n.Title = "middle"; });
        await SeedAsync(n => { n.Category = scope; n.CreatedAt = baseTime.AddMinutes(-2); n.Title = "oldest"; });

        using var client = _factory.CreateTenantClient();

        using var page1 = await client.GetAsync("/api/notifications?category=RulebookApproval&limit=2");
        Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
        using var doc1 = await JsonDocument.ParseAsync(await page1.Content.ReadAsStreamAsync());
        var items = doc1.RootElement.GetProperty("notifications");
        Assert.Equal(2, items.GetArrayLength());

        var first = items[0];
        Assert.True(first.TryGetProperty("id", out _));           // camelCase envelope
        Assert.True(first.TryGetProperty("createdAt", out _));
        Assert.False(first.TryGetProperty("linkHref", out _));    // WhenWritingNull omits nulls
        Assert.False(first.TryGetProperty("readAt", out _));

        var cursor = doc1.RootElement.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));

        using var page2 = await client.GetAsync($"/api/notifications?category=RulebookApproval&limit=2&cursor={cursor}");
        using var doc2 = await JsonDocument.ParseAsync(await page2.Content.ReadAsStreamAsync());
        var rest = doc2.RootElement.GetProperty("notifications");
        Assert.Equal(1, rest.GetArrayLength());
    }

    [Fact]
    public async Task List_UnreadFilter_ExcludesReadRows()
    {
        await SeedAsync(n => { n.Title = "unread-one"; });
        await SeedAsync(n => { n.Title = "already-read"; n.ReadAt = DateTimeOffset.UtcNow; });

        using var client = _factory.CreateTenantClient();
        using var resp = await client.GetAsync("/api/notifications?unread=true&limit=100");
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());

        foreach (var item in doc.RootElement.GetProperty("notifications").EnumerateArray())
            Assert.False(item.TryGetProperty("readAt", out _)); // every returned row is unread
    }

    [Fact]
    public async Task UnreadCount_ReturnsUnreadAndUnacked()
    {
        await SeedAsync(n => { n.Title = "u1"; });
        await SeedAsync(n => { n.Title = "u2-ack"; n.RequiresAck = true; n.Urgency = NotificationUrgency.Critical; });

        using var client = _factory.CreateTenantClient();
        using var resp = await client.GetAsync("/api/notifications/unread-count");
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());

        Assert.True(doc.RootElement.GetProperty("unread").GetInt32() >= 2);
        Assert.True(doc.RootElement.GetProperty("unacked").GetInt32() >= 1);
    }

    [Fact]
    public async Task Read_IsIdempotent()
    {
        var id = await SeedAsync(n => { n.Title = "read-me"; });
        using var client = _factory.CreateTenantClient();

        using var first = await client.PostAsync($"/api/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstDoc = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        Assert.True(firstDoc.RootElement.TryGetProperty("readAt", out _));

        using var second = await client.PostAsync($"/api/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // no-op second time
    }

    [Fact]
    public async Task Ack_SetsFields_AndAudits()
    {
        var before = await AuditCountAsync(AuditAction.NotificationAcknowledged);
        var id = await SeedAsync(n => { n.Title = "ack-me"; n.RequiresAck = true; n.Urgency = NotificationUrgency.Critical; });

        using var client = _factory.CreateTenantClient();
        using var resp = await client.PostAsync($"/api/notifications/{id}/ack", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("acknowledgedAt", out _));
        Assert.Equal(before + 1, await AuditCountAsync(AuditAction.NotificationAcknowledged));
    }

    [Fact]
    public async Task Ack_OnNonAckableRow_Returns400()
    {
        var id = await SeedAsync(n => { n.Title = "not-ackable"; n.RequiresAck = false; });
        using var client = _factory.CreateTenantClient();

        using var resp = await client.PostAsync($"/api/notifications/{id}/ack", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("not_ackable", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Bulk_ClinicalRowsWithoutConfirm_Returns400ConfirmationRequired()
    {
        var critical = await SeedAsync(n => { n.Title = "crit"; n.Category = NotificationCategory.CriticalResult; n.Urgency = NotificationUrgency.Critical; n.RequiresAck = true; });
        var routine = await SeedAsync(n => { n.Title = "routine"; });

        using var client = _factory.CreateTenantClient();
        using var resp = await client.PostAsJsonAsync("/api/notifications/bulk",
            new { ids = new[] { critical, routine }, action = "read", confirm = false });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("confirmation_required", doc.RootElement.GetProperty("kind").GetString());
        var offending = doc.RootElement.GetProperty("ids").EnumerateArray().Select(x => x.GetGuid()).ToList();
        Assert.Contains(critical, offending);
    }

    [Fact]
    public async Task Bulk_WithConfirm_Succeeds_AndAuditsBulkAction()
    {
        var before = await AuditCountAsync(AuditAction.NotificationBulkAction);
        var critical = await SeedAsync(n => { n.Title = "crit2"; n.Category = NotificationCategory.CriticalResult; n.Urgency = NotificationUrgency.Critical; n.RequiresAck = true; });
        var routine = await SeedAsync(n => { n.Title = "routine2"; });

        using var client = _factory.CreateTenantClient();
        using var resp = await client.PostAsJsonAsync("/api/notifications/bulk",
            new { ids = new[] { critical, routine }, action = "read", confirm = true });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("updated").GetInt32() >= 2);
        Assert.Equal(before + 1, await AuditCountAsync(AuditAction.NotificationBulkAction));
    }

    [Fact]
    public async Task Prefs_RoundTrip_AndRejectMutingCriticalClass()
    {
        using var client = _factory.CreateTenantClient();

        using var put = await client.PutAsJsonAsync("/api/notifications/prefs",
            new { mutedCategoriesCsv = "PeerReview", dndStartMinutes = 60, dndEndMinutes = 480, dndTimeZone = "", pushEnabled = false, emailEnabled = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var get = await client.GetAsync("/api/notifications/prefs");
        using var doc = await JsonDocument.ParseAsync(await get.Content.ReadAsStreamAsync());
        Assert.Equal("PeerReview", doc.RootElement.GetProperty("mutedCategoriesCsv").GetString());
        Assert.Equal(60, doc.RootElement.GetProperty("dndStartMinutes").GetInt32());
        Assert.False(doc.RootElement.GetProperty("pushEnabled").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("emailEnabled").GetBoolean());

        using var bad = await client.PutAsJsonAsync("/api/notifications/prefs",
            new { mutedCategoriesCsv = "CriticalResult" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var badDoc = await JsonDocument.ParseAsync(await bad.Content.ReadAsStreamAsync());
        Assert.Equal("mandatory_category", badDoc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task OtherUsersRow_IsInvisible_And404OnRead()
    {
        // A row owned by the admin user must be invisible to the radiologist client.
        var id = await SeedAsync(n => { n.Title = "admins-row"; }, userId: _factory.SeedAdmin.Id);

        using var client = _factory.CreateTenantClient();

        using var read = await client.PostAsync($"/api/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }
}
