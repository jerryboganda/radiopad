using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-34 BILL-005 — <see cref="IAiUsageStore.SummariseAsync"/> must price
/// the per-provider rollup against the tenant's current
/// <see cref="ProviderConfig.CostPerInputKToken"/> /
/// <see cref="ProviderConfig.CostPerOutputKToken"/> (USD per 1K tokens) and
/// flag rows that no longer have a matching provider config as
/// <c>unpriced=true</c> with zero cost.
/// </summary>
public class Iter34UsageCostRollupTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter34UsageCostRollupTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SummariseAsync_PricesByProvider_AndFlagsUnpriced()
    {
        // Fresh tenant so we don't see any AiRequest rows from other tests.
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IAiUsageStore>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Slug = $"cost-{tenantId:N}",
            DisplayName = "Cost Rollup",
        });

        // Priced provider — currently configured for the tenant.
        db.Providers.Add(new ProviderConfig
        {
            TenantId = tenantId,
            Name = "AnthropicPaid",
            Adapter = "anthropic",
            Model = "claude-mock",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
            CostPerInputKToken = 3.00m,   // $0.003 / token
            CostPerOutputKToken = 15.00m, // $0.015 / token
        });
        // The "RetiredVendor" historical rows below have no matching
        // ProviderConfig — they must surface as unpriced.

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);

        // Two requests against the priced provider.
        db.AiRequests.Add(new AiRequest
        {
            TenantId = tenantId,
            UserId = Guid.Empty,
            Provider = "AnthropicPaid",
            Model = "claude-mock",
            Mode = "draft",
            Status = "ok",
            CreatedAt = t0,
            InputTokens = 1000,
            OutputTokens = 500,
            InputHash = "a",
            OutputHash = "b",
        });
        db.AiRequests.Add(new AiRequest
        {
            TenantId = tenantId,
            UserId = Guid.Empty,
            Provider = "AnthropicPaid",
            Model = "claude-mock",
            Mode = "draft",
            Status = "ok",
            CreatedAt = t0.AddMinutes(1),
            InputTokens = 2000,
            OutputTokens = 100,
            InputHash = "c",
            OutputHash = "d",
        });
        // One request against a retired provider name.
        db.AiRequests.Add(new AiRequest
        {
            TenantId = tenantId,
            UserId = Guid.Empty,
            Provider = "RetiredVendor",
            Model = "legacy-mock",
            Mode = "draft",
            Status = "ok",
            CreatedAt = t0.AddMinutes(2),
            InputTokens = 5000,
            OutputTokens = 7500,
            InputHash = "e",
            OutputHash = "f",
        });
        await db.SaveChangesAsync();

        var summary = await usage.SummariseAsync(tenantId, from: null, to: null, ct: default);

        Assert.Equal(3, summary.TotalRequests);
        Assert.Equal(2, summary.ByProvider.Count);

        var paid = summary.ByProvider.Single(p => p.Provider == "AnthropicPaid");
        Assert.False(paid.Unpriced);
        Assert.Equal(3000L, paid.InputTokens);
        Assert.Equal(600L, paid.OutputTokens);
        // (3000 / 1000) * 3.00 = 9.00 ; (600 / 1000) * 15.00 = 9.00 ; total = 18.00.
        Assert.Equal(9.00m, paid.CostInputUsd);
        Assert.Equal(9.00m, paid.CostOutputUsd);
        Assert.Equal(18.00m, paid.CostTotalUsd);

        var retired = summary.ByProvider.Single(p => p.Provider == "RetiredVendor");
        Assert.True(retired.Unpriced);
        Assert.Equal(0m, retired.CostInputUsd);
        Assert.Equal(0m, retired.CostOutputUsd);
        Assert.Equal(0m, retired.CostTotalUsd);

        Assert.Equal(18.00m, summary.CostTotalUsd);
    }
}
