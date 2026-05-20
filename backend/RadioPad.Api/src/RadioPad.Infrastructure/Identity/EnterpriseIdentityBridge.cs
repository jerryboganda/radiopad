using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Identity;

public static class EnterpriseIdentityBridge
{
    public const string LegacyProviderKey = "legacy-user";
    public const string LegacyIssuer = "radiopad";

    public static async Task EnsureSchemaAsync(RadioPadDbContext db, CancellationToken ct)
    {
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return;

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "GlobalUsers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GlobalUsers" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "PrimaryEmail" TEXT NOT NULL,
                "NormalizedEmail" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "LastLoginAt" INTEGER NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ExternalIdentities" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ExternalIdentities" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "GlobalUserId" TEXT NOT NULL,
                "ProviderKey" TEXT NOT NULL,
                "Issuer" TEXT NOT NULL,
                "Subject" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "NormalizedEmail" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "ClaimsJson" TEXT NOT NULL,
                "LastSeenAt" INTEGER NULL,
                "RevokedAt" INTEGER NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "TenantMemberships" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TenantMemberships" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "GlobalUserId" TEXT NOT NULL,
                "TenantId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "IsDefault" INTEGER NOT NULL,
                "JoinedAt" INTEGER NOT NULL,
                "RemovedAt" INTEGER NULL,
                "SessionEpoch" INTEGER NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AuthSessions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AuthSessions" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "GlobalUserId" TEXT NOT NULL,
                "TenantMembershipId" TEXT NULL,
                "TenantId" TEXT NULL,
                "UserId" TEXT NULL,
                "TokenHash" TEXT NOT NULL,
                "Method" TEXT NOT NULL,
                "IssuedAt" INTEGER NOT NULL,
                "ExpiresAt" INTEGER NOT NULL,
                "RevokedAt" INTEGER NULL,
                "RevocationReason" TEXT NOT NULL,
                "DeviceFingerprintHash" TEXT NOT NULL,
                "IpHash" TEXT NOT NULL,
                "UserAgentHash" TEXT NOT NULL,
                "SessionEpochAtIssue" INTEGER NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_GlobalUsers_NormalizedEmail" ON "GlobalUsers" ("NormalizedEmail");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExternalIdentities_ProviderKey_Issuer_Subject" ON "ExternalIdentities" ("ProviderKey", "Issuer", "Subject");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ExternalIdentities_GlobalUserId_ProviderKey" ON "ExternalIdentities" ("GlobalUserId", "ProviderKey");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ExternalIdentities_NormalizedEmail" ON "ExternalIdentities" ("NormalizedEmail");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantMemberships_TenantId_UserId" ON "TenantMemberships" ("TenantId", "UserId");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantMemberships_GlobalUserId_TenantId" ON "TenantMemberships" ("GlobalUserId", "TenantId");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_TenantMemberships_TenantId_Status" ON "TenantMemberships" ("TenantId", "Status");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuthSessions_TokenHash" ON "AuthSessions" ("TokenHash");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AuthSessions_GlobalUserId_ExpiresAt" ON "AuthSessions" ("GlobalUserId", "ExpiresAt");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AuthSessions_TenantId_UserId_ExpiresAt" ON "AuthSessions" ("TenantId", "UserId", "ExpiresAt");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AuthSessions_RevokedAt_ExpiresAt" ON "AuthSessions" ("RevokedAt", "ExpiresAt");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_MagicLinks_TokenHash" ON "MagicLinks" ("TokenHash");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceAuth_DeviceCodeHash" ON "DeviceAuth" ("DeviceCodeHash");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceAuth_UserCode" ON "DeviceAuth" ("UserCode");""", ct);
    }

    public static async Task EnsureForAllUsersAsync(RadioPadDbContext db, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        foreach (var user in users)
        {
            await EnsureMembershipForUserAsync(db, user, ct);
        }
    }

    public static async Task<TenantMembership> EnsureMembershipForUserAsync(
        RadioPadDbContext db,
        User user,
        CancellationToken ct)
    {
        var existing = await db.TenantMemberships
            .FirstOrDefaultAsync(m => m.TenantId == user.TenantId && m.UserId == user.Id, ct);
        if (existing is not null)
        {
            await EnsureLegacyExternalIdentityAsync(db, existing.GlobalUserId, user, ct);
            return existing;
        }

        var global = new GlobalUser
        {
            PrimaryEmail = user.Email,
            NormalizedEmail = NormalizeEmail(user.Email),
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
        };
        db.GlobalUsers.Add(global);

        var membership = new TenantMembership
        {
            GlobalUserId = global.Id,
            TenantId = user.TenantId,
            UserId = user.Id,
            Status = user.IsActive ? "active" : "deprovisioned",
            IsDefault = true,
            JoinedAt = user.CreatedAt,
            SessionEpoch = user.SessionEpoch,
        };
        db.TenantMemberships.Add(membership);

        AddLegacyExternalIdentity(db, global.Id, user);
        try
        {
            await db.SaveChangesAsync(ct);
            return membership;
        }
        catch (DbUpdateException)
        {
            Detach(db, global, membership);
            var raced = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == user.TenantId && m.UserId == user.Id, ct);
            if (raced is not null)
                return raced;
            throw;
        }
    }

    public static async Task<AuthSession> RecordAuthSessionAsync(
        RadioPadDbContext db,
        User user,
        string bearerToken,
        string method,
        DateTimeOffset expiresAt,
        CancellationToken ct,
        string? deviceFingerprint = null,
        string? ip = null,
        string? userAgent = null)
    {
        var tokenHash = Sha256Hex(bearerToken);
        var existing = await db.AuthSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        if (existing is not null)
            return existing;

        var membership = await EnsureMembershipForUserAsync(db, user, ct);
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession
        {
            GlobalUserId = membership.GlobalUserId,
            TenantMembershipId = membership.Id,
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = tokenHash,
            Method = method,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            DeviceFingerprintHash = HashOptional(deviceFingerprint),
            IpHash = HashOptional(ip),
            UserAgentHash = HashOptional(userAgent),
            SessionEpochAtIssue = user.SessionEpoch,
        };
        db.AuthSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
            return session;
        }
        catch (DbUpdateException)
        {
            db.Entry(session).State = EntityState.Detached;
            var raced = await db.AuthSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
            if (raced is not null)
                return raced;
            throw;
        }
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static string HashOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Sha256Hex(value.Trim());

    private static async Task EnsureLegacyExternalIdentityAsync(
        RadioPadDbContext db,
        Guid globalUserId,
        User user,
        CancellationToken ct)
    {
        var subject = user.Id.ToString("N");
        var exists = await db.ExternalIdentities.AnyAsync(
            e => e.ProviderKey == LegacyProviderKey && e.Issuer == LegacyIssuer && e.Subject == subject,
            ct);
        if (exists)
            return;

        AddLegacyExternalIdentity(db, globalUserId, user);
        await db.SaveChangesAsync(ct);
    }

    private static void AddLegacyExternalIdentity(RadioPadDbContext db, Guid globalUserId, User user)
    {
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            GlobalUserId = globalUserId,
            ProviderKey = LegacyProviderKey,
            Issuer = LegacyIssuer,
            Subject = user.Id.ToString("N"),
            Email = user.Email,
            NormalizedEmail = NormalizeEmail(user.Email),
            DisplayName = user.DisplayName,
        });
    }

    private static void Detach(RadioPadDbContext db, params Entity[] entities)
    {
        foreach (var entity in entities)
        {
            var entry = db.Entry(entity);
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }

        foreach (var entry in db.ChangeTracker.Entries<ExternalIdentity>().Where(e => e.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }
}
