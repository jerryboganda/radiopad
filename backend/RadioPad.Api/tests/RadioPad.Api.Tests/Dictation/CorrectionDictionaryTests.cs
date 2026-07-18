using System.Linq;
using RadioPad.Application.Dictation;
using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §6 / F7 — the correction dictionary applied deterministically BEFORE the LLM. Phase 0 wires
/// the org (tenant) lexicon; the per-user personal-override layer follows.
/// </summary>
public class CorrectionDictionaryTests
{
    [Fact]
    public void FromLexicon_Maps_Rows_With_A_Replacement_To_Rules()
    {
        var lexicon = new[]
        {
            new TenantLexicon { Term = "hypo dense", Replacement = "hypodense" },
            new TenantLexicon { Term = "US", Replacement = "ultrasound" },
        };

        var rules = CorrectionDictionary.FromLexicon(lexicon);

        Assert.Contains(rules, r => r.From == "hypo dense" && r.To == "hypodense");
        Assert.Contains(rules, r => r.From == "US" && r.To == "ultrasound");
    }

    [Fact]
    public void FromLexicon_Excludes_Rows_Without_A_Replacement()
    {
        // A forbidden term with no replacement can't be corrected deterministically (no target).
        var lexicon = new[] { new TenantLexicon { Term = "ca", Forbidden = true, Replacement = "" } };
        Assert.Empty(CorrectionDictionary.FromLexicon(lexicon));
    }

    [Fact]
    public void FromLexicon_Orders_Longer_Phrases_First()
    {
        var lexicon = new[]
        {
            new TenantLexicon { Term = "gall bladder", Replacement = "gallbladder" },
            new TenantLexicon { Term = "gall bladder wall", Replacement = "GB wall" },
        };

        var rules = CorrectionDictionary.FromLexicon(lexicon);

        // Longer phrase first so it is not pre-empted by the shorter rule.
        Assert.Equal("gall bladder wall", rules[0].From);
    }

    [Fact]
    public void FromLexicon_Null_Yields_Empty()
    {
        Assert.Empty(CorrectionDictionary.FromLexicon(null));
    }

    [Fact]
    public void Resolved_Rules_Apply_Through_The_PassThrough()
    {
        var lexicon = new[] { new TenantLexicon { Term = "hypo dense", Replacement = "hypodense" } };
        var rules = CorrectionDictionary.FromLexicon(lexicon);

        var result = new DeterministicPassThrough().Process("the lesion is hypo dense", rules);

        Assert.Contains("hypodense", result.CorrectedTranscript);
    }

    // ── per-user layer (F7b) ─────────────────────────────────────────────

    [Fact]
    public void Resolve_Merges_Org_And_User_With_User_Winning_For_Same_Term()
    {
        var org = new[]
        {
            new TenantLexicon { Term = "US", Replacement = "ultrasound" },
            new TenantLexicon { Term = "hypo dense", Replacement = "hypodense" },
        };
        var user = new[] { new UserCorrection { From = "US", To = "US scan" } };

        var rules = CorrectionDictionary.Resolve(org, user);

        Assert.Contains(rules, r => r.From == "US" && r.To == "US scan");          // user overrides org
        Assert.Contains(rules, r => r.From == "hypo dense" && r.To == "hypodense"); // org entry kept
        Assert.Single(rules, r => string.Equals(r.From, "US", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_Null_Inputs_Yield_Empty()
    {
        Assert.Empty(CorrectionDictionary.Resolve(null, null));
    }
}
