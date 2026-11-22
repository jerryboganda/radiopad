using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Middleware;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Middleware;

public class RequestLookupsTests
{
    [Fact]
    public async Task SecondLookupInTheSameRequest_IsServedFromMemory_NotTheDatabase()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        await using var db = new RadioPadDbContext(new DbContextOptionsBuilder<RadioPadDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        var tenant = new Tenant { Slug = "memo", DisplayName = "Memo" };
        db.Tenants.Add(tenant);
        var user = new User { TenantId = tenant.Id, Email = "a@memo.test", DisplayName = "A" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var ctx = new DefaultHttpContext();
        var t1 = await RequestLookups.TenantAsync(ctx, db, "memo", default);
        var u1 = await RequestLookups.UserAsync(ctx, db, tenant.Id, "a@memo.test", default);
        Assert.NotNull(t1);
        Assert.NotNull(u1);

        // Remove the rows behind the cache's back: a repeat lookup that still succeeds never touched the database.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Users; DELETE FROM Tenants;");
        Assert.Same(t1, await RequestLookups.TenantAsync(ctx, db, "memo", default));
        Assert.Same(u1, await RequestLookups.UserAsync(ctx, db, tenant.Id, "a@memo.test", default));

        // A different request (new HttpContext) sees the database again, so revocations are never cached across requests.
        Assert.Null(await RequestLookups.TenantAsync(new DefaultHttpContext(), db, "memo", default));
    }
}
