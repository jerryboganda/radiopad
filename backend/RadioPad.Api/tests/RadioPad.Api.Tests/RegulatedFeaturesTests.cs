using System.Linq;
using RadioPad.Application.Governance;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// PRD Phase 3 — regulated, assist-only capabilities.
///
/// <para><b>These assertions were INVERTED on 2026-07-20.</b> They previously required the gate to
/// fail CLOSED (off unless explicitly enabled). The operator directed that no capability be withheld
/// — the deploying organisation holds the applicable UKCA / MHRA / CE / FDA clearances — so the
/// default is now ENABLED and only an explicit <c>false</c> disables a feature.</para>
///
/// <para>Worth recording why this mattered: the old default was never actually enforced. The gate
/// had ZERO production call sites and its doc comment named a field on the wrong entity
/// (<c>Tenant.FeatureFlagsJson</c>; flags live on <c>TenantSettings</c>), so every capability ran
/// unconditionally while the admin panel told customers they shipped off pending review. These
/// tests passed throughout — they only ever exercised the pure function, never its use. The gate is
/// now genuinely consulted at every entry point, which is what makes the admin toggles real.</para>
///
/// <para>Unchanged: these capabilities stay suggestion-only and are never auto-applied, and
/// RadioPad still never auto-signs a report (safety boundary #1).</para>
/// </summary>
public class RegulatedFeaturesTests
{
    [Fact]
    public void All_Features_Are_Enabled_By_Default()
    {
        // No flags configured ⇒ nothing explicitly switched off ⇒ everything available.
        foreach (RegulatedFeature f in System.Enum.GetValues(typeof(RegulatedFeature)))
        {
            Assert.True(RegulatedFeatures.IsEnabled(null, f));
            Assert.True(RegulatedFeatures.IsEnabled("{}", f));
        }
    }

    [Fact]
    public void A_Feature_Is_Disabled_Only_When_Its_Flag_Is_Explicitly_False()
    {
        var json = "{\"regulated.autoImpression\": false}";
        Assert.False(RegulatedFeatures.IsEnabled(json, RegulatedFeature.AutoImpression));
        // Switching one capability off must not disturb the others.
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.CriticalFindingFlagging));
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.FollowUpStandardisation));
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.IntervalChangeTracking));
    }

    [Fact]
    public void An_Explicit_True_Flag_Is_On()
    {
        var json = "{\"regulated.autoImpression\": true}";
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.AutoImpression));
    }

    [Fact]
    public void Accepts_A_Stringified_Boolean_Flag()
    {
        Assert.False(RegulatedFeatures.IsEnabled(
            "{\"regulated.followUpStandardisation\": \"false\"}", RegulatedFeature.FollowUpStandardisation));
        Assert.True(RegulatedFeatures.IsEnabled(
            "{\"regulated.followUpStandardisation\": \"true\"}", RegulatedFeature.FollowUpStandardisation));
    }

    [Fact]
    public void Malformed_Json_Leaves_Features_Enabled()
    {
        // A corrupted flags blob carries no explicit override, so it must not strip a capability out
        // from under a radiologist mid-session. Disabling one is an administrative act.
        Assert.True(RegulatedFeatures.IsEnabled("{not valid", RegulatedFeature.IntervalChangeTracking));
        Assert.True(RegulatedFeatures.IsEnabled("[]", RegulatedFeature.IntervalChangeTracking)); // not an object
    }

    [Fact]
    public void Describe_Agrees_With_IsEnabled_For_Every_Feature()
    {
        // The admin panel renders Describe() while the endpoints enforce IsEnabled(). If the two
        // disagree the UI shows a state the enforcement path does not honour — precisely the
        // dead-toggle class of defect this work exists to remove.
        foreach (var json in new string?[] { null, "{}", "{\"regulated.autoImpression\": false}", "{bad" })
        {
            var described = RegulatedFeatures.Describe(json);
            Assert.Equal(4, described.Count);
            foreach (var d in described)
                Assert.Equal(RegulatedFeatures.IsEnabled(json, d.Feature), d.Enabled);
        }
    }

    [Fact]
    public void Describe_Reflects_An_Explicitly_Disabled_Feature()
    {
        var described = RegulatedFeatures.Describe("{\"regulated.autoImpression\": false}");
        Assert.False(described.Single(d => d.Feature == RegulatedFeature.AutoImpression).Enabled);
        Assert.True(described.Single(d => d.Feature == RegulatedFeature.IntervalChangeTracking).Enabled);
        Assert.All(described, d => Assert.StartsWith(RegulatedFeatures.Prefix, d.Key));
    }
}
