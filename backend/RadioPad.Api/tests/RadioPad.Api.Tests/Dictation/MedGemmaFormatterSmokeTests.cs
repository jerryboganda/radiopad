using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// End-to-end smoke for the OFFLINE formatter: the real MedGemma GGUF running on a real
/// llama-server, driven through the whole §4.2 pipeline (§5.2 pass-through → formatter → §5.3
/// validation-diff → §5.6 sentinel).
///
/// <para>GATED on <c>RADIOPAD_MEDGEMMA_SMOKE_URL</c> so it is a no-op everywhere except the
/// dedicated workflow that provisions the ~2.5 GB model and the llama-server runtime. That job runs
/// on a GitHub runner, never on a developer machine — the model download and CPU inference are far
/// too heavy for a laptop.</para>
///
/// <para>Every assertion here corresponds to something that actually went wrong during the first
/// manual end-to-end run, which is why they are worth the runner minutes: the report was discarded
/// for NOT fabricating a section, and before that the engine routing silently used the wrong
/// model. These are integration properties — each unit was green while the system was broken.</para>
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class MedGemmaFormatterSmokeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public MedGemmaFormatterSmokeTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    /// <summary>Minimal factory so the provider can be built without the full DI graph.</summary>
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(10) };
    }

    private const string Dictation =
        "CT chest with contrast. There is a three point two centimeter nodule in the right upper " +
        "lobe. No pneumothorax. Impression acute pulmonary embolism in the left lower lobe.";

    [Fact]
    public async Task Formats_A_Dictation_Without_Fabricating_Or_Losing_Clinical_Content()
    {
        var url = Environment.GetEnvironmentVariable("RADIOPAD_MEDGEMMA_SMOKE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            if (SttSmokeGate.IsRequired())
                Assert.Fail("RADIOPAD_MEDGEMMA_SMOKE_URL is unset but RADIOPAD_STT_SMOKE_REQUIRE=1 " +
                            "demands a real offline-formatter run.");
            return; // normal CI — no llama-server, skip
        }

        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, "1")
            .Set(LocalMedGemmaFormatter.UrlEnv, url);

        var provider = new LlamaCppProvider(new SingleClientFactory(), NullLogger<LlamaCppProvider>.Instance);
        var formatter = new LocalMedGemmaFormatter(new IAiProviderAdapter[] { provider });
        Assert.True(formatter.Available, "the offline formatter should be available when enabled");

        var engine = new DictationEngineService(
            new DeterministicPassThrough(),
            new DictationValidationService(),
            new LateralityNegationSentinel());

        var context = new DictationFormatContext(
            Modality: "CT",
            BodyPart: "Chest",
            Indication: "Shortness of breath",
            SectionKeys: DictationGrammar.DefaultSections,
            Grammar: DictationGrammar.ReportSectionsGbnf);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var draft = await engine.RunAsync(
            Dictation, context, Array.Empty<CorrectionRule>(), patientSex: "F", formatter, CancellationToken.None);
        sw.Stop();
        // Greppable marker consumed by offline-formatter-smoke.yml, which publishes the number to
        // its job summary — closes the "MedGemma latency unmeasured" half of §7 item 4. The server
        // is already warm when this runs, so this is per-call §4.2 pipeline latency, not model load.
        _out.WriteLine($"medgemma_format_ms={sw.ElapsedMilliseconds}");

        foreach (var (k, v) in draft.DraftSections)
            _out.WriteLine($"[{k}] {v}");
        _out.WriteLine($"accepted={draft.Accepted} fallback={draft.UsedFallback} " +
                       $"violations={draft.Violations.Count} sentinel={draft.SentinelWarnings.Count}");

        // 1) The structured report must actually be USED. This failed on the first real run: a
        //    clean report was discarded because the dictation contained no "recommendations".
        Assert.True(draft.Accepted, "a faithful report must be accepted, not discarded to fallback");
        Assert.False(draft.UsedFallback);

        var all = string.Join("\n", draft.DraftSections.Values);

        // 2) §5.2 — the spoken measurement must be normalized and survive verbatim.
        Assert.Contains("3.2 cm", all, StringComparison.OrdinalIgnoreCase);

        // 3) Laterality must not be flipped — the classic, and most dangerous, formatter failure.
        Assert.Contains("right upper lobe", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left lower lobe", all, StringComparison.OrdinalIgnoreCase);

        // 4) Nothing fabricated: no BLOCKING violation may survive on a faithful dictation.
        Assert.DoesNotContain(draft.Violations, v => v.IsBlocking);

        // 5) It must be a real structured report, not the raw transcript echoed into one section.
        Assert.True(draft.DraftSections.Count > 1, "expected multiple populated report sections");

        // 6) Never auto-signed — the draft always requires review (safety boundary #1/#2).
        Assert.True(draft.RequiresReview);
    }

    /// <summary>
    /// Adversarial: a dictation with no measurement at all must not gain one. This is the property
    /// §5.3 exists for, exercised against the real model rather than a stub.
    /// </summary>
    [Fact]
    public async Task Does_Not_Invent_A_Measurement_That_Was_Never_Dictated()
    {
        var url = Environment.GetEnvironmentVariable("RADIOPAD_MEDGEMMA_SMOKE_URL");
        if (string.IsNullOrWhiteSpace(url)) return;

        using var env = new SttSmokeGate.EnvScope()
            .Set(LocalMedGemmaFormatter.EnabledEnv, "1")
            .Set(LocalMedGemmaFormatter.UrlEnv, url);

        var provider = new LlamaCppProvider(new SingleClientFactory(), NullLogger<LlamaCppProvider>.Instance);
        var formatter = new LocalMedGemmaFormatter(new IAiProviderAdapter[] { provider });
        var engine = new DictationEngineService(
            new DeterministicPassThrough(), new DictationValidationService(), new LateralityNegationSentinel());

        var context = new DictationFormatContext(
            "CT", "Chest", "Screening", DictationGrammar.DefaultSections, DictationGrammar.ReportSectionsGbnf);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var draft = await engine.RunAsync(
            "CT chest. There is a nodule in the right upper lobe. No pneumothorax.",
            context, Array.Empty<CorrectionRule>(), null, formatter, CancellationToken.None);
        sw.Stop();
        _out.WriteLine($"medgemma_format_ms={sw.ElapsedMilliseconds}");

        foreach (var (k, v) in draft.DraftSections) _out.WriteLine($"[{k}] {v}");

        // Either the model added nothing (accepted), or §5.3 caught it and failed safe. What must
        // NEVER happen is an invented measurement being accepted into the draft.
        if (draft.Accepted)
            Assert.DoesNotMatch(@"\d+(\.\d+)?\s*(mm|cm)\b", string.Join("\n", draft.DraftSections.Values));
        else
            Assert.Contains(draft.Violations, v => v.IsBlocking);
    }
}
