using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// Unit tests for the durable side of the desktop↔phone companion handshake. The
/// pairing rules are the security boundary: a phone must pair as the SAME
/// (tenant, user) that advertised the code, and only within the code's TTL. A
/// wrong / expired / cross-user code must be indistinguishable to the caller
/// (all → null → 404) so a prober learns nothing.
/// </summary>
public class CompanionSessionServiceTests
{
    private static RadioPadDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        // Keep the in-memory connection open for the context's lifetime, then build
        // the schema straight from the model (mirrors the repo's EnsureCreated path).
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Create_MintsAdvertisingSession_WithSixCharCode()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var session = await svc.CreateAsync(tenantId, userId, "Reading Room PC", default);

        Assert.Equal(CompanionSessionStatus.Advertising, session.Status);
        Assert.Equal(6, session.PairingCode.Length);
        Assert.Matches("^[A-Z0-9]{6}$", session.PairingCode);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Null(session.PairedAt);
        Assert.Null(session.CompanionDeviceName);
        Assert.Equal("Reading Room PC", session.HostDeviceName);
    }

    [Fact]
    public async Task Create_MintsDistinctCodes()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var a = await svc.CreateAsync(tenantId, userId, "Desk A", default);
        var b = await svc.CreateAsync(tenantId, userId, "Desk B", default);

        Assert.NotEqual(a.PairingCode, b.PairingCode);
    }

    [Fact]
    public async Task Pair_WithValidCode_TransitionsToPaired()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        var paired = await svc.PairAsync(tenantId, userId, created.PairingCode, "John's iPhone", default);

        Assert.NotNull(paired);
        Assert.Equal(created.Id, paired!.Id);
        Assert.Equal(CompanionSessionStatus.Paired, paired.Status);
        Assert.Equal("John's iPhone", paired.CompanionDeviceName);
        Assert.NotNull(paired.PairedAt);
    }

    [Fact]
    public async Task Pair_IsCaseInsensitive_OnTheCode()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        var paired = await svc.PairAsync(tenantId, userId, created.PairingCode.ToLowerInvariant(), "Phone", default);

        Assert.NotNull(paired);
        Assert.Equal(CompanionSessionStatus.Paired, paired!.Status);
    }

    [Fact]
    public async Task Pair_WithUnknownCode_ReturnsNull()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await svc.CreateAsync(tenantId, userId, "Desk", default);

        var paired = await svc.PairAsync(tenantId, userId, "ZZZZZZ", "Phone", default);

        Assert.Null(paired);
    }

    [Fact]
    public async Task Pair_WithExpiredCode_ReturnsNull_AndMarksExpired()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        // Force the advertisement past its TTL.
        created.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var paired = await svc.PairAsync(tenantId, userId, created.PairingCode, "Phone", default);

        Assert.Null(paired);
        var reloaded = await db.CompanionSessions.FindAsync(created.Id);
        Assert.Equal(CompanionSessionStatus.Expired, reloaded!.Status);
    }

    [Fact]
    public async Task Pair_AlreadyPairedCode_ReturnsNull()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        var first = await svc.PairAsync(tenantId, userId, created.PairingCode, "Phone A", default);
        var second = await svc.PairAsync(tenantId, userId, created.PairingCode, "Phone B", default);

        Assert.NotNull(first);
        Assert.Null(second); // a code is single-use
    }

    [Fact]
    public async Task Pair_FromDifferentUser_IsRejected()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, owner, "Desk", default);

        var paired = await svc.PairAsync(tenantId, otherUser, created.PairingCode, "Phone", default);

        Assert.Null(paired);
        var reloaded = await db.CompanionSessions.FindAsync(created.Id);
        Assert.Equal(CompanionSessionStatus.Advertising, reloaded!.Status); // untouched
    }

    [Fact]
    public async Task Pair_FromDifferentTenant_IsRejected()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var ownerTenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(ownerTenant, userId, "Desk", default);

        var paired = await svc.PairAsync(Guid.NewGuid(), userId, created.PairingCode, "Phone", default);

        Assert.Null(paired);
    }

    [Fact]
    public async Task End_IsScopedAndIdempotent()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        // A different user cannot end it.
        var foreign = await svc.EndAsync(tenantId, Guid.NewGuid(), created.Id, default);
        Assert.Null(foreign);

        var ended = await svc.EndAsync(tenantId, userId, created.Id, default);
        Assert.NotNull(ended);
        Assert.Equal(CompanionSessionStatus.Ended, ended!.Status);

        // Idempotent.
        var again = await svc.EndAsync(tenantId, userId, created.Id, default);
        Assert.Equal(CompanionSessionStatus.Ended, again!.Status);
    }

    [Fact]
    public async Task Get_ReturnsOnlyOwnedSession()
    {
        using var db = NewDb();
        var svc = new CompanionSessionService(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var created = await svc.CreateAsync(tenantId, userId, "Desk", default);

        Assert.NotNull(await svc.GetAsync(tenantId, userId, created.Id, default));
        Assert.Null(await svc.GetAsync(tenantId, Guid.NewGuid(), created.Id, default));
        Assert.Null(await svc.GetAsync(Guid.NewGuid(), userId, created.Id, default));
    }
}
