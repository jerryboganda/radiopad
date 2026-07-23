using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Jobs;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Push;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Jobs;

/// <summary>
/// NOTIF-003/004 (PR-N4) — unit tests for <see cref="NotificationChannelDispatchJob"/> on a real
/// SQLite <see cref="RadioPadDbContext"/> with recording push + email senders. Covers the
/// per-recipient preference gate, the PHI-min tier (no FindingSummary/accession on push), and the
/// not-configured-sender path (audits <see cref="AuditAction.NotificationDeliveryFailed"/> and does
/// NOT re-throw, so Hangfire never retries a config error).
/// </summary>
public sealed class NotificationChannelDispatchJobTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"radiopad-chan-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _sp;
    private readonly RecordingAuditLog _audit = new();
    private readonly RecordingPushSender _push = new("ios");
    private readonly RecordingEmailSender _email = new();
    private readonly Tenant _tenant = new() { Slug = "chan", DisplayName = "Chan" };

    public NotificationChannelDispatchJobTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RadioPadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IAuditLog>(_audit);
        services.AddSingleton<IPushSender>(_push);
        services.AddSingleton<PushSenderRegistry>();
        services.AddSingleton<IEmailSender>(_email);
        services.AddLogging();
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var db = Db(scope);
        db.Database.EnsureCreated();
        db.Tenants.Add(_tenant);
        db.SaveChanges();
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static RadioPadDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

    private NotificationChannelDispatchJob NewJob() =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<ILoggerFactory>().CreateLogger<NotificationChannelDispatchJob>());

    private async Task<Guid> SeedAsync(Action<NotificationPreference>? pref, bool withDevice, string body)
    {
        using var scope = _sp.CreateScope();
        var db = Db(scope);
        var user = new User
        {
            TenantId = _tenant.Id,
            Email = "chan-user@radiopad.local",
            DisplayName = "Chan User",
            Role = UserRole.Radiologist,
            IsActive = true,
        };
        db.Users.Add(user);

        var notif = new Notification
        {
            TenantId = _tenant.Id,
            UserId = user.Id,
            Category = NotificationCategory.CriticalResult,
            Urgency = NotificationUrgency.Critical,
            Title = "Critical result on your report",
            Body = body,
            RequiresAck = true,
        };
        db.Notifications.Add(notif);

        if (pref is not null)
        {
            var p = new NotificationPreference { TenantId = _tenant.Id, UserId = user.Id };
            pref(p);
            db.NotificationPreferences.Add(p);
        }
        if (withDevice)
            db.PushDevices.Add(new PushDevice { TenantId = _tenant.Id, UserId = user.Id, Platform = "ios", Token = "tok-123" });

        await db.SaveChangesAsync();
        return notif.Id;
    }

    [Fact]
    public async Task DeliverPush_RespectsPreference_SkipsWhenDisabled()
    {
        var id = await SeedAsync(p => p.PushEnabled = false, withDevice: true, body: "Large right pneumothorax");

        await NewJob().DeliverPushAsync(id, CancellationToken.None);

        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task DeliverPush_PhiMinBody_NoFindingSummaryOrAccession()
    {
        var id = await SeedAsync(
            p => p.PushEnabled = true, withDevice: true, body: "Large right pneumothorax, accession ACC-12345");

        await NewJob().DeliverPushAsync(id, CancellationToken.None);

        var payload = Assert.Single(_push.Sent);
        // Generic category phrase, and NEVER the finding narrative or accession.
        Assert.Equal("Critical result", payload.Title);
        Assert.DoesNotContain("pneumothorax", payload.Title);
        Assert.DoesNotContain("pneumothorax", payload.Body);
        Assert.DoesNotContain("ACC-12345", payload.Title);
        Assert.DoesNotContain("ACC-12345", payload.Body);
        Assert.Equal("criticalresult", payload.Kind);
        Assert.Equal(id.ToString(), payload.EntityId);
    }

    [Fact]
    public async Task DeliverEmail_RespectsPreference()
    {
        var id = await SeedAsync(p => p.EmailEnabled = false, withDevice: false, body: "Large right pneumothorax");
        var job = NewJob();

        await job.DeliverEmailAsync(id, CancellationToken.None);
        Assert.Empty(_email.Sent); // email opt-in is off

        using (var scope = _sp.CreateScope())
        {
            var pref = await Db(scope).NotificationPreferences.FirstAsync();
            pref.EmailEnabled = true;
            await Db(scope).SaveChangesAsync();
        }

        await job.DeliverEmailAsync(id, CancellationToken.None);
        var msg = Assert.Single(_email.Sent);
        Assert.Equal("chan-user@radiopad.local", msg.To);
        Assert.DoesNotContain("pneumothorax", msg.HtmlBody);
        Assert.DoesNotContain("pneumothorax", msg.Subject);
    }

    [Fact]
    public async Task DeliverPush_SenderNotConfigured_AuditsDeliveryFailed_NoRetry()
    {
        _push.IsConfigured = false; // SendAsync throws PushNotConfiguredException
        var id = await SeedAsync(p => p.PushEnabled = true, withDevice: true, body: "finding");

        // Must NOT throw — a config error is not transient, so Hangfire must never retry it.
        await NewJob().DeliverPushAsync(id, CancellationToken.None);

        Assert.Empty(_push.Sent);
        Assert.Contains(_audit.Events, e => e.Action == AuditAction.NotificationDeliveryFailed);
    }

    // ── recording doubles ────────────────────────────────────────────────────

    private sealed class RecordingPushSender : IPushSender
    {
        public RecordingPushSender(string platform) => Platform = platform;
        public string Platform { get; }
        public bool IsConfigured { get; set; } = true;
        public List<PushPayload> Sent { get; } = new();

        public Task SendAsync(string deviceToken, PushPayload payload, CancellationToken ct)
        {
            if (!IsConfigured) throw new PushNotConfiguredException("not configured");
            Sent.Add(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool Result { get; set; } = true;

        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.FromResult(Result);
        }
    }

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
