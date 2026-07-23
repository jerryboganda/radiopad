using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// NOTIF-001 unit tests for <see cref="NotificationProducer"/> on a real SQLite
/// <see cref="RadioPadDbContext"/> with a recording audit log and a real
/// <see cref="AiJobEventBus"/>. Covers the suppression matrix (dedupe, mute,
/// critical-class bypass, storm coalescing), the DND channel-decision record, and
/// permission-holder fan-out over <c>RolePermissionMap</c>.
/// </summary>
public sealed class NotificationProducerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"radiopad-notif-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _sp;
    private readonly RecordingAuditLog _audit = new();
    private readonly Tenant _tenant = new() { Slug = "notif", DisplayName = "Notif" };
    private readonly User _user = new() { Email = "notif-user@radiopad.local", DisplayName = "Recipient", Role = UserRole.Radiologist };

    public NotificationProducerTests()
    {
        _user.TenantId = _tenant.Id;

        var services = new ServiceCollection();
        services.AddDbContext<RadioPadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IAuditLog>(_audit);
        services.AddLogging();
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        db.Database.EnsureCreated();
        db.Tenants.Add(_tenant);
        db.Users.Add(_user);
        db.SaveChanges();
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static AiJobEventBus NewBus() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    private NotificationProducer NewProducer(IAiJobEventBus bus) =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(), bus,
            _sp.GetRequiredService<ILoggerFactory>().CreateLogger<NotificationProducer>());

    private RadioPadDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

    private NotificationDraft Draft(
        NotificationCategory category = NotificationCategory.PeerReview,
        NotificationUrgency urgency = NotificationUrgency.Info,
        string? dedupeKey = null, Guid? user = null) =>
        new(_tenant.Id, user ?? _user.Id, category, urgency, "Workflow event", "", DedupeKey: dedupeKey);

    private async Task SeedPrefAsync(Action<NotificationPreference> configure)
    {
        using var scope = _sp.CreateScope();
        var pref = new NotificationPreference { TenantId = _tenant.Id, UserId = _user.Id };
        configure(pref);
        Db(scope).NotificationPreferences.Add(pref);
        await Db(scope).SaveChangesAsync();
    }

    private async Task<int> CountAsync(Func<IQueryable<Notification>, IQueryable<Notification>> filter)
    {
        using var scope = _sp.CreateScope();
        return await filter(Db(scope).Notifications).CountAsync();
    }

    [Fact]
    public async Task CreateAsync_PersistsRow_AndPublishesToBus()
    {
        var bus = NewBus();
        using var firehose = bus.Subscribe(null, null);
        var producer = NewProducer(bus);

        var created = await producer.CreateAsync(Draft(), CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(1, await CountAsync(q => q));

        Assert.True(firehose.Reader.TryRead(out var evt));
        Assert.Equal("notification", evt!.EventType);

        // Creation is always audited (NOTIF-008); the audit carries no Title/Body text.
        var audited = Assert.Single(_audit.Events, e => e.Action == AuditAction.NotificationCreated);
        Assert.DoesNotContain("Workflow event", audited.DetailsJson);
    }

    [Fact]
    public async Task CreateAsync_DedupeKey_SuppressesTheSecond()
    {
        var producer = NewProducer(NewBus());

        var first = await producer.CreateAsync(Draft(dedupeKey: "same-key"), CancellationToken.None);
        var second = await producer.CreateAsync(Draft(dedupeKey: "same-key"), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, await CountAsync(q => q));
    }

    [Fact]
    public async Task CreateAsync_MutedNonCriticalCategory_IsSuppressed()
    {
        await SeedPrefAsync(p => p.MutedCategoriesCsv = "PeerReview");
        var producer = NewProducer(NewBus());

        var created = await producer.CreateAsync(Draft(NotificationCategory.PeerReview), CancellationToken.None);

        Assert.Null(created);
        Assert.Equal(0, await CountAsync(q => q));
    }

    [Fact]
    public async Task CreateAsync_CriticalClass_IsNeverSuppressibleByMute()
    {
        // Both critical paths: Critical urgency, and a category in the tenant's critical CSV.
        await SeedPrefAsync(p => p.MutedCategoriesCsv = "PeerReview,CriticalResult");
        var producer = NewProducer(NewBus());

        var byUrgency = await producer.CreateAsync(
            Draft(NotificationCategory.PeerReview, NotificationUrgency.Critical), CancellationToken.None);
        var byCategory = await producer.CreateAsync(
            Draft(NotificationCategory.CriticalResult, NotificationUrgency.Info), CancellationToken.None);

        Assert.NotNull(byUrgency);
        Assert.NotNull(byCategory);
        Assert.Equal(2, await CountAsync(q => q));
    }

    [Fact]
    public async Task CreateAsync_Dnd_SuppressesChannelsNotRow_AndRecordsThem()
    {
        // A full-day DND window guarantees "now" is inside it regardless of wall-clock.
        await SeedPrefAsync(p => { p.DndStartMinutes = 0; p.DndEndMinutes = 1440; p.DndTimeZone = ""; });
        var producer = NewProducer(NewBus());

        var nonCritical = await producer.CreateAsync(Draft(NotificationCategory.PeerReview, NotificationUrgency.Info), CancellationToken.None);
        var critical = await producer.CreateAsync(Draft(NotificationCategory.PeerReview, NotificationUrgency.Critical), CancellationToken.None);

        // The inbox row + SSE always happen — DND only holds dispatch channels.
        Assert.NotNull(nonCritical);
        Assert.NotNull(critical);
        Assert.Equal(2, await CountAsync(q => q));

        var events = _audit.Events.Where(e => e.Action == AuditAction.NotificationCreated).ToList();
        Assert.Equal(2, events.Count);
        // Non-critical during DND records suppressed channels; critical bypasses DND entirely.
        Assert.Contains(events, e => DndChannels(e).Length > 0);
        Assert.Contains(events, e => DndChannels(e).Length == 0);
    }

    [Fact]
    public async Task CreateAsync_StormCap_CoalescesOverflowIntoOneSystemRow()
    {
        var producer = NewProducer(NewBus());

        for (var i = 0; i < 65; i++)
            await producer.CreateAsync(Draft(NotificationCategory.PeerReview), CancellationToken.None);

        // The per-minute cap is 60; the overflow (5) collapses into a single System/Warning row.
        Assert.Equal(60, await CountAsync(q => q.Where(n => n.Category == NotificationCategory.PeerReview)));
        Assert.Equal(1, await CountAsync(q => q.Where(n =>
            n.Category == NotificationCategory.System && n.Urgency == NotificationUrgency.Warning)));
    }

    [Fact]
    public async Task NotifyPermissionHoldersAsync_ResolvesViaRolePermissionMap_AndExcludesActor()
    {
        User actor, otherHolder, nonHolder;
        using (var scope = _sp.CreateScope())
        {
            var db = Db(scope);
            actor = new User { TenantId = _tenant.Id, Email = "a@x", DisplayName = "Actor", Role = UserRole.ReportingAdmin };
            otherHolder = new User { TenantId = _tenant.Id, Email = "b@x", DisplayName = "Other", Role = UserRole.ReportingAdmin };
            nonHolder = new User { TenantId = _tenant.Id, Email = "c@x", DisplayName = "Rad", Role = UserRole.Radiologist };
            db.Users.AddRange(actor, otherHolder, nonHolder);
            await db.SaveChangesAsync();
        }

        var producer = NewProducer(NewBus());
        await producer.NotifyPermissionHoldersAsync(
            _tenant.Id, RbacPermission.RulebooksManage, excludeUserId: actor.Id,
            uid => new NotificationDraft(_tenant.Id, uid, NotificationCategory.RulebookApproval,
                NotificationUrgency.Info, "Rulebook approved", ""),
            CancellationToken.None);

        // ReportingAdmin holds RulebooksManage; Radiologist does not; the actor is excluded.
        Assert.Equal(1, await CountAsync(q => q.Where(n => n.UserId == otherHolder.Id)));
        Assert.Equal(0, await CountAsync(q => q.Where(n => n.UserId == actor.Id)));
        Assert.Equal(0, await CountAsync(q => q.Where(n => n.UserId == nonHolder.Id)));
    }

    private static string[] DndChannels(AuditEvent e)
    {
        using var doc = JsonDocument.Parse(e.DetailsJson);
        return doc.RootElement.TryGetProperty("dndSuppressedChannels", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : Array.Empty<string>();
    }

    /// <summary>An in-memory <see cref="IAuditLog"/> that captures appended events for assertion.</summary>
    private sealed class RecordingAuditLog : IAuditLog
    {
        private readonly ConcurrentQueue<AuditEvent> _events = new();
        public IReadOnlyList<AuditEvent> Events => _events.ToArray();

        public Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            _events.Enqueue(evt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(
            Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(_events.Where(e => e.TenantId == tenantId).ToArray());

        public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
