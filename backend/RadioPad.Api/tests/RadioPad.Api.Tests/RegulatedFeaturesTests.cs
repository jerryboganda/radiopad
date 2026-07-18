using System.Linq;
using RadioPad.Application.Governance;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// PRD Phase 3 — regulated features must ship OFF by default and fail safe. A missing, false, or
/// malformed flag must never read as enabled: these gate assist-only clinical suggestions that
/// require regulatory review, so a default-on bug would be a safety issue.
/// </summary>
public class RegulatedFeaturesTests
{
    [Fact]
    public void All_Features_Are_Off_By_Default()
    {
        foreach (RegulatedFeature f in System.Enum.GetValues(typeof(RegulatedFeature)))
        {
            Assert.False(RegulatedFeatures.IsEnabled(null, f));
            Assert.False(RegulatedFeatures.IsEnabled("{}", f));
        }
    }

    [Fact]
    public void A_Feature_Is_Enabled_Only_When_Its_Flag_Is_Explicitly_True()
    {
        var json = "{\"regulated.criticalFindingFlagging\": true}";
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.CriticalFindingFlagging));
        // Other features remain off.
        Assert.False(RegulatedFeatures.IsEnabled(json, RegulatedFeature.AutoImpression));
    }

    [Fact]
    public void An_Explicit_False_Flag_Is_Off()
    {
        var json = "{\"regulated.autoImpression\": false}";
        Assert.False(RegulatedFeatures.IsEnabled(json, RegulatedFeature.AutoImpression));
    }

    [Fact]
    public void Accepts_A_Stringified_Boolean_Flag()
    {
        var json = "{\"regulated.followUpStandardisation\": \"true\"}";
        Assert.True(RegulatedFeatures.IsEnabled(json, RegulatedFeature.FollowUpStandardisation));
    }

    [Fact]
    public void Malformed_Json_Fails_Safe_To_Off()
    {
        Assert.False(RegulatedFeatures.IsEnabled("{not valid", RegulatedFeature.IntervalChangeTracking));
        Assert.False(RegulatedFeatures.IsEnabled("[]", RegulatedFeature.IntervalChangeTracking)); // not an object
    }

    [Fact]
    public void Describe_Lists_Every_Feature_With_Its_State()
    {
        var json = "{\"regulated.autoImpression\": true}";
        var described = RegulatedFeatures.Describe(json);

        Assert.Equal(4, described.Count);
        Assert.True(described.Single(d => d.Feature == RegulatedFeature.AutoImpression).Enabled);
        Assert.False(described.Single(d => d.Feature == RegulatedFeature.IntervalChangeTracking).Enabled);
        Assert.All(described, d => Assert.StartsWith(RegulatedFeatures.Prefix, d.Key));
    }
}
