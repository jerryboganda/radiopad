namespace RadioPad.Application.Dictation;

/// <summary>
/// The OPTIONAL on-device MedGemma report formatter (dictation brief §2.2). <see cref="Available"/>
/// is true only on the desktop sidecar when the offline formatter is enabled and the bundled
/// llama-server is reachable; everywhere else the cloud formatter is used and this stays inert. PHI
/// never leaves the device — the implementation enforces a loopback endpoint.
/// </summary>
public interface ILocalReportFormatter : IDictationFormatter
{
    /// <summary>
    /// Whether the on-device formatter can run on this host at all — i.e. whether the explicit
    /// on-device endpoint (<c>POST /api/dictation/draft-local</c>) should serve rather than 503.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Whether the on-device formatter should also be the DEFAULT for report-scoped drafting.
    ///
    /// <para>Deliberately separate from <see cref="Available"/>. Operator decision D1 is that cloud
    /// AI stays primary and the local formatter is an OPTIONAL path the user selects; but the draft
    /// service used to choose with <c>Available ? local : cloud</c>, so merely making the capability
    /// reachable would have silently rerouted every desktop report draft to MedGemma. Two different
    /// questions — "can this run here?" and "should it run by default?" — must not share one flag.</para>
    /// </summary>
    bool PreferredForReportDrafts { get; }
}
