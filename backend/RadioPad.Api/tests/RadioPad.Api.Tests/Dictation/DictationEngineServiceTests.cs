using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §4.2 — the report-assembly pipeline: deterministic pass-through (§5.2) → formatter →
/// validation-diff (§5.3) → laterality/negation/gender sentinel (§5.6), with a fail-safe fallback
/// to the dictionary-corrected raw transcript when validation rejects the LLM output.
/// </summary>
public class DictationEngineServiceTests
{
    private sealed class FakeFormatter : IDictationFormatter
    {
        public string? Received;
        public Func<string, IReadOnlyDictionary<string, string>> Map = _ => new Dictionary<string, string>();

        public Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct)
        {
            Received = protectedTranscript;
            return Task.FromResult(new FormatterOutput(Map(protectedTranscript), "local-medgemma", "medgemma-1.5-4b-q4", 42));
        }
    }

    private static DictationFormatContext Ctx() =>
        new("CT", "Chest", "cough", new[] { "findings", "impression" }, DictationGrammar.ReportSectionsGbnf);

    private static DictationEngineService Engine() =>
        new(new DeterministicPassThrough(), new DictationValidationService(), new LateralityNegationSentinel());

    [Fact]
    public async Task Accepts_And_Returns_Formatted_Sections_When_Faithful()
    {
        var fmt = new FakeFormatter
        {
            Map = _ => new Dictionary<string, string>
            {
                ["findings"] = "3.2 cm nodule in the right upper lobe.",
                ["impression"] = "Right upper lobe nodule.",
            },
        };

        var draft = await Engine().RunAsync(
            "3.2 cm nodule in the right upper lobe", Ctx(), Array.Empty<CorrectionRule>(), "male", fmt, CancellationToken.None);

        Assert.True(draft.Accepted);
        Assert.False(draft.UsedFallback);
        Assert.Equal("3.2 cm nodule in the right upper lobe.", draft.DraftSections["findings"]);
        Assert.Equal("local-medgemma", draft.Provider);
    }

    [Fact]
    public async Task Falls_Back_To_Corrected_Transcript_When_Validation_Rejects()
    {
        var fmt = new FakeFormatter
        {
            Map = _ => new Dictionary<string, string>
            {
                ["findings"] = "5 cm mass in the right upper lobe.",   // 5 cm was never dictated
                ["impression"] = "Mass.",
            },
        };

        var draft = await Engine().RunAsync(
            "nodule in the right upper lobe", Ctx(), Array.Empty<CorrectionRule>(), null, fmt, CancellationToken.None);

        Assert.False(draft.Accepted);
        Assert.True(draft.UsedFallback);
        Assert.Equal("nodule in the right upper lobe", draft.DraftSections["findings"]);
        Assert.Contains(draft.Violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
    }

    [Fact]
    public async Task Surfaces_Sentinel_Warnings_On_Laterality_Flip()
    {
        var fmt = new FakeFormatter
        {
            Map = _ => new Dictionary<string, string>
            {
                ["findings"] = "Mass in the left kidney.",     // dictation said right
                ["impression"] = "Left renal mass.",
            },
        };

        var draft = await Engine().RunAsync(
            "mass in the right kidney", Ctx(), Array.Empty<CorrectionRule>(), null, fmt, CancellationToken.None);

        Assert.Contains(draft.SentinelWarnings, w => w.Kind == SentinelKind.Laterality);
        Assert.True(draft.RequiresReview);
    }

    [Fact]
    public async Task Applies_Corrections_Before_The_Formatter_Sees_The_Text()
    {
        var fmt = new FakeFormatter
        {
            Map = t => new Dictionary<string, string> { ["findings"] = t, ["impression"] = "x" },
        };
        var rules = new[] { new CorrectionRule("hypo dense", "hypodense") };

        var draft = await Engine().RunAsync(
            "hypo dense lesion", Ctx(), rules, null, fmt, CancellationToken.None);

        Assert.Contains("hypodense", fmt.Received);
        Assert.DoesNotContain("hypo dense", fmt.Received);
    }
}
