using Microsoft.EntityFrameworkCore;

namespace RadioPad.Infrastructure.Persistence;

/// <summary>
/// Findings Library schema bootstrap. Mirrors the pattern established by
/// <c>EnterpriseIdentityBridge.EnsureSchemaAsync</c>: the tables are declared as
/// EF entities (so LINQ works) but created idempotently with raw SQL at startup
/// rather than through a migration, which keeps existing SQLite files upgradable
/// in place without a migration step on the VPS.
/// </summary>
public static class FindingsLibrarySchema
{
    public static async Task EnsureSchemaAsync(RadioPadDbContext db, CancellationToken ct)
    {
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return;

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Snippets" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Snippets" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "TenantId" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Modality" TEXT NOT NULL,
                "BodyPart" TEXT NOT NULL,
                "Category" TEXT NULL,
                "SectionsJson" TEXT NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_Snippets_Tenant_Name" ON "Snippets" ("TenantId", "Name");
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LibraryFavorites" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LibraryFavorites" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "TenantId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityKey" TEXT NOT NULL
            );
            """, ct);

        // One star per (user, item) — the toggle endpoint relies on this to stay idempotent.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LibraryFavorites_User_Entity"
                ON "LibraryFavorites" ("TenantId", "UserId", "EntityType", "EntityKey");
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LibraryRecentUses" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LibraryRecentUses" PRIMARY KEY,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "TenantId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityKey" TEXT NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_LibraryRecentUses_User_CreatedAt"
                ON "LibraryRecentUses" ("TenantId", "UserId", "CreatedAt");
            """, ct);
    }
}
