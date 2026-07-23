using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Jobs;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-N2 — <see cref="AiCostRollupJob"/>. Drives <c>RunForDayAsync</c> directly. Confirms the
/// per-(tenant, provider, model) counts and token sums, that out-of-window rows are excluded,
/// and that a re-run upserts in place (no duplicate rollup rows).
/// </summary>
public class AiCostRollupJobTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AiCostRollupJobTests(RadioPadAppFactory f) => _factory = f;

    private static AiRequest Req(Guid tenantId, DateTimeOffset createdAt, string provider, string model, int input, int output) => new()
    {
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        Provider = provider,
        Model = model,
        InputTokens = input,
        OutputTokens = output,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    [Fact]
    public async Task RunForDay_AggregatesPerTuple_ExcludesOtherDays_AndReRunUpsertsNoDupes()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var atNoon = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var tenantId = _factory.SeedTenant.Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.AiRequests.Add(Req(tenantId, atNoon, "openai", "gpt-4", 10, 20));
            db.AiRequests.Add(Req(tenantId, atNoon.AddHours(1), "openai", "gpt-4", 5, 15));
            db.AiRequests.Add(Req(tenantId, atNoon.AddHours(2), "anthropic", "claude", 100, 200));
            // Out of window (today) — must be excluded.
            db.AiRequests.Add(Req(tenantId, DateTimeOffset.UtcNow, "openai", "gpt-4", 999, 999));
            await db.SaveChangesAsync();
        }

        var job = _factory.Services.GetRequiredService<AiCostRollupJob>();
        await job.RunForDayAsync(day, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var openai = await db.AiUsageRollups.SingleAsync(r =>
                r.TenantId == tenantId && r.Date == day && r.Provider == "openai" && r.Model == "gpt-4");
            Assert.Equal(2, openai.RequestCount);
            Assert.Equal(15, openai.InputTokens);
            Assert.Equal(35, openai.OutputTokens);

            var anthropic = await db.AiUsageRollups.SingleAsync(r =>
                r.TenantId == tenantId && r.Date == day && r.Provider == "anthropic" && r.Model == "claude");
            Assert.Equal(1, anthropic.RequestCount);
            Assert.Equal(100, anthropic.InputTokens);
            Assert.Equal(200, anthropic.OutputTokens);
        }

        // Re-run: upsert in place, no duplicate rows.
        await job.RunForDayAsync(day, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rowCount = await db.AiUsageRollups.CountAsync(r => r.TenantId == tenantId && r.Date == day);
            Assert.Equal(2, rowCount);
            var openai = await db.AiUsageRollups.SingleAsync(r =>
                r.TenantId == tenantId && r.Date == day && r.Provider == "openai" && r.Model == "gpt-4");
            Assert.Equal(2, openai.RequestCount);
            Assert.Equal(15, openai.InputTokens);
        }
    }
}
