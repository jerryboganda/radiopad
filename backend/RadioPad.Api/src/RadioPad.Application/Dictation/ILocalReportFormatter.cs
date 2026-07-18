namespace RadioPad.Application.Dictation;

/// <summary>
/// The OPTIONAL on-device MedGemma report formatter (dictation brief §2.2). <see cref="Available"/>
/// is true only on the desktop sidecar when the offline formatter is enabled and the bundled
/// llama-server is reachable; everywhere else the cloud formatter is used and this stays inert. PHI
/// never leaves the device — the implementation enforces a loopback endpoint.
/// </summary>
public interface ILocalReportFormatter : IDictationFormatter
{
    bool Available { get; }
}
