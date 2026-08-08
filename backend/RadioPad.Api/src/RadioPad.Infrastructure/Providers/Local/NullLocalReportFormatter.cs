using RadioPad.Application.Dictation;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Placeholder <see cref="ILocalReportFormatter"/> registered while no on-device report formatter is
/// wired up. Always unavailable, so <c>POST /api/dictation/draft-local</c> returns 503 and the cloud
/// formatter stays the only active path — mirrors how every other on-device capability degrades when
/// its runtime/model has not been provisioned. Replace this registration in
/// <c>RadioPad.Api/Program.cs</c> when a real on-device formatter is added.
/// </summary>
public sealed class NullLocalReportFormatter : ILocalReportFormatter
{
    public bool Available => false;

    public bool PreferredForReportDrafts => false;

    public Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct) =>
        throw new InvalidOperationException($"{nameof(NullLocalReportFormatter)} is unavailable; callers must check {nameof(Available)} before invoking {nameof(FormatAsync)}.");
}
