using System.Text;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Dictation;

/// <summary>Produces a safety-wrapped, editable dictation draft for one report (brief §4.2).</summary>
public interface IDictationDraftService
{
    Task<DictationDraft> BuildDraftAsync(
        Tenant tenant, User user, Report report, string rawDictation,
        IReadOnlyList<CorrectionRule> corrections, CancellationToken ct);
}

/// <summary>
/// Brief §4.2 — wraps the EXISTING report formatter (<see cref="IDictationCleanupService"/>, which
/// already routes through the PHI-gated <c>AiGateway</c>) with the deterministic safety pipeline
/// (§5.2 pass-through → §5.3 validation → §5.6 sentinel) and records the §5.7 audit trail. Reuses
/// the cloud formatter today; the same wrapper serves the optional local MedGemma formatter once the
/// desktop llama-server path is wired (P0.4). NEVER signs — the draft feeds the §5.5 sign-off gate.
/// </summary>
public sealed class DictationDraftService : IDictationDraftService
{
    private readonly IDictationCleanupService _cleanup;
    private readonly DictationEngineService _engine;
    private readonly IDictationAuditStore _audit;
    private readonly ILocalReportFormatter _localFormatter;

    public DictationDraftService(
        IDictationCleanupService cleanup,
        DictationEngineService engine,
        IDictationAuditStore audit,
        ILocalReportFormatter localFormatter)
    {
        _cleanup = cleanup;
        _engine = engine;
        _audit = audit;
        _localFormatter = localFormatter;
    }

    public async Task<DictationDraft> BuildDraftAsync(
        Tenant tenant, User user, Report report, string rawDictation,
        IReadOnlyList<CorrectionRule> corrections, CancellationToken ct)
    {
        var context = new DictationFormatContext(
            Modality: report.Study?.Modality ?? string.Empty,
            BodyPart: report.Study?.BodyPart ?? string.Empty,
            Indication: report.Indication ?? string.Empty,
            SectionKeys: DictationGrammar.DefaultSections,
            // §5.4 — used by the local MedGemma formatter; the cloud formatter ignores it.
            Grammar: DictationGrammar.ReportSectionsGbnf);

        // Cloud is the default report formatter (decision D1); the on-device one takes over here
        // only when explicitly preferred, NOT merely because it is available. This used to read
        // `Available ? local : cloud`, which conflated "can run here" with "should run by default"
        // — so switching the capability on for the on-device endpoint would have silently rerouted
        // every desktop report draft to MedGemma. The deterministic safety layers wrap whichever
        // one runs.
        IDictationFormatter formatter = _localFormatter.PreferredForReportDrafts
            ? _localFormatter
            : new CleanupServiceFormatter(_cleanup, tenant, user, report);

        // patientSex is threaded through once the Study sex field is confirmed (P0.4 wiring); the
        // §5.6 gender sentinel is exercised by unit tests meanwhile.
        var draft = await _engine.RunAsync(rawDictation, context, corrections, patientSex: null, formatter, ct);

        await _audit.AppendAsync(new DictationAuditRecord(
            ReportId: report.Id.ToString(),
            RawTranscript: draft.RawTranscript,
            CorrectedTranscript: draft.CorrectedTranscript,
            FinalSections: draft.DraftSections,
            Diff: BuildDiff(draft),
            TemplateId: report.RulebookId?.ToString(),
            SttModel: "text-input",
            FormatterProvider: draft.Provider,
            FormatterModel: draft.Model,
            Accepted: draft.Accepted,
            TimestampUtc: DateTime.UtcNow.ToString("o")), ct);

        return draft;
    }

    private static string BuildDiff(DictationDraft draft)
    {
        var sb = new StringBuilder();
        sb.Append("— dictation (corrected) —\n").Append(draft.CorrectedTranscript).Append("\n\n— report —\n");
        foreach (var kv in draft.DraftSections)
            sb.Append('[').Append(kv.Key).Append("] ").Append(kv.Value).Append('\n');
        if (!draft.Accepted)
            sb.Append("\n[validation rejected — showing the dictionary-corrected transcript]");
        return sb.ToString();
    }

    /// <summary>Adapts the existing cleanup formatter to <see cref="IDictationFormatter"/>.</summary>
    private sealed class CleanupServiceFormatter : IDictationFormatter
    {
        private readonly IDictationCleanupService _cleanup;
        private readonly Tenant _tenant;
        private readonly User _user;
        private readonly Report _report;

        public CleanupServiceFormatter(IDictationCleanupService cleanup, Tenant tenant, User user, Report report)
        {
            _cleanup = cleanup;
            _tenant = tenant;
            _user = user;
            _report = report;
        }

        public async Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct)
        {
            var r = await _cleanup.CleanupAsync(_tenant, _user, _report, protectedTranscript, ct);
            var sections = new Dictionary<string, string>
            {
                ["indication"] = r.Indication,
                ["technique"] = r.Technique,
                ["findings"] = r.Findings,
                ["impression"] = r.Impression,
                ["recommendations"] = r.Recommendations,
            };
            return new FormatterOutput(sections, r.Provider, r.Model, r.LatencyMs);
        }
    }
}

/// <summary>
/// In-memory dictation audit store — the DI-safe default on web/server (no filesystem writes). The
/// desktop wires the encrypted, on-disk <c>FileDictationAuditStore</c> (§5.7); see
/// IMPLEMENTATION_NOTES.md for the key-management step.
/// </summary>
public sealed class InMemoryDictationAuditStore : IDictationAuditStore
{
    private readonly object _gate = new();
    private readonly List<DictationAuditEntry> _entries = new();
    private string _lastHash = DictationAuditChain.GenesisHash;

    public Task<DictationAuditEntry> AppendAsync(DictationAuditRecord record, CancellationToken ct)
    {
        lock (_gate)
        {
            var entry = DictationAuditChain.Append(record, _lastHash);
            _entries.Add(entry);
            _lastHash = entry.Hash;
            return Task.FromResult(entry);
        }
    }

    public Task<IReadOnlyList<DictationAuditEntry>> ReadAllAsync(CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<DictationAuditEntry>>(_entries.ToList());
    }

    public Task<bool> VerifyAsync(CancellationToken ct)
    {
        lock (_gate)
            return Task.FromResult(DictationAuditChain.Verify(_entries.ToList()));
    }
}
