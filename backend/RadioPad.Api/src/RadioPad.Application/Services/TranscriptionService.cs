using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services;

/// <summary>
/// Phase B (dictation transcription) — orchestrates the four-call UBAG flow to
/// turn a report's dictation audio into a free-text transcript:
/// <list type="number">
/// <item>Create a <c>medical_transcription</c> job (job FIRST, so the worker
/// can wait on the named artifact).</item>
/// <item>Upload the audio bytes as the <c>dictation.webm</c> artifact (audio
/// SECOND).</item>
/// <item>The worker attaches + scrapes (no RadioPad involvement).</item>
/// <item>Poll the job to terminal; the transcript is the job output text.</item>
/// </list>
/// Provider selection mirrors <see cref="DictationCleanupService"/> (cheapest
/// matching provider via <see cref="IProviderRouter"/>), so PHI routing is
/// enforced exactly like the existing text dictation path — no extra gate. Only
/// the SHA-256 of the transcript is ever persisted — never the transcript itself.
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    /// <summary>Single-path-segment artifact key the worker waits on.</summary>
    public const string AudioArtifactKey = "dictation.webm";

    /// <summary>Hard cap on a single dictation upload (32 MiB).</summary>
    public const long MaxAudioBytes = 32L * 1024 * 1024;

    private const string TranscriptionPrompt =
        "Transcribe the attached radiology dictation audio verbatim into clean, " +
        "grammatical clinical prose. Preserve every measurement, laterality, " +
        "negation, and clinical hedge exactly as spoken. Do not add findings that " +
        "were not dictated. Output only the transcript text — no preface, no commentary.";

    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    private readonly IUbagClient _ubag;
    private readonly IProviderRouter _router;
    private readonly IAuditLog _audit;
    private readonly ILogger<TranscriptionService> _log;

    public TranscriptionService(
        IUbagClient ubag,
        IProviderRouter router,
        IAuditLog audit,
        ILogger<TranscriptionService> log)
    {
        _ubag = ubag;
        _router = router;
        _audit = audit;
        _log = log;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Tenant tenant,
        User user,
        Report report,
        Stream audio,
        string fileName,
        long sizeBytes,
        string contentType,
        CancellationToken ct)
    {
        if (audio is null)
            throw new ArgumentNullException(nameof(audio));
        if (sizeBytes <= 0)
            throw new ArgumentException("Audio payload is empty.", nameof(sizeBytes));
        if (sizeBytes > MaxAudioBytes)
            throw new ArgumentException($"Audio payload exceeds the {MaxAudioBytes} byte limit.", nameof(sizeBytes));

        // PHI routing mirrors DictationCleanupService exactly: report content
        // drives provider selection, and the router refuses to hand PHI content to
        // a non-PHI-approved provider (returning null -> the throw below). There is
        // deliberately NO extra audio-specific gate: the spoken dictation is the
        // radiologist's de-identified findings, handled like every other AI call.
        var containsPhi = ReportingService.ContainsPhi(report);

        var provider = await _router.SelectAsync(tenant, containsPhi, ct)
            ?? throw new ProviderPolicyException(
                "No enabled provider matches the tenant's PHI / compliance requirements.");

        var target = ResolveTarget(provider);
        var idempotencyKey = BuildIdempotencyKey(tenant.Id, report.Id, fileName, sizeBytes);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Create the transcription job FIRST so the worker can wait on the artifact.
        var created = await _ubag.CreateTranscriptionJobAsync(
            new UbagTranscriptionRequest(
                Target: target,
                AudioArtifactKey: AudioArtifactKey,
                Prompt: TranscriptionPrompt,
                ClientRequestId: idempotencyKey),
            idempotencyKey,
            ct);

        if (string.IsNullOrWhiteSpace(created.Id))
            throw new ProviderTransportException("ubag: transcription job missing id");

        // 2. Upload the audio bytes SECOND, keyed as dictation.webm.
        await _ubag.UploadJobArtifactAsync(
            created.Id,
            AudioArtifactKey,
            audio,
            contentType,
            sizeBytes,
            idempotencyKey,
            ct);

        // 3. Worker attaches + scrapes (no RadioPad involvement).
        // 4. Poll to terminal with a 120s budget, then read the transcript.
        var terminal = await PollToTerminalAsync(created, ct);

        if (!string.IsNullOrWhiteSpace(terminal.ManualAction))
            throw new ProviderPolicyException("ubag: manual_action_required");
        if (!string.IsNullOrWhiteSpace(terminal.Error)
            || terminal.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            throw new ProviderTransportException($"ubag: {terminal.Error ?? terminal.Status}");
        if (string.IsNullOrWhiteSpace(terminal.Output))
            throw new ProviderTransportException("ubag: empty_transcript");

        sw.Stop();
        var transcript = terminal.Output;
        var latencyMs = terminal.LatencyMs ?? (int)sw.ElapsedMilliseconds;

        // PHI-free operational log (sizes + provenance only — never transcript text).
        _log.LogInformation(
            "Transcribed dictation audio for report {ReportId} via {Provider}/{Target} ({SizeBytes} bytes, {LatencyMs} ms)",
            report.Id, provider.Name, target, sizeBytes, latencyMs);

        // Audit — store the SHA-256 of the transcript, NEVER the transcript text.
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.AudioTranscribed,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                provider = provider.Name,
                target,
                artifactKey = AudioArtifactKey,
                sizeBytes,
                transcriptSha256 = Sha256(transcript),
                latencyMs,
            }),
        }, ct);

        return new TranscriptionResult(
            Text: transcript,
            Provider: provider.Name,
            Model: target,
            LatencyMs: latencyMs);
    }

    /// <summary>
    /// Resolves the UBAG target for a provider. Mirrors the Infrastructure-side
    /// <c>UbagProviderAdapter.ResolveTarget</c> default (the provider's
    /// <c>Model</c>, or <c>gemini_web</c> when unset). The selected provider
    /// already passed the router's enable/compliance filter, so no further
    /// allow-list gate is duplicated across the layering boundary here.
    /// </summary>
    private static string ResolveTarget(ProviderConfig provider)
        => string.IsNullOrWhiteSpace(provider.Model) ? "gemini_web" : provider.Model.Trim();

    private async Task<UbagJob> PollToTerminalAsync(UbagJob initial, CancellationToken ct)
    {
        var timeoutMs = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_UBAG_TIMEOUT_MS"), out var ms) && ms > 0
            ? ms
            : 120_000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        var current = initial;
        while (!current.Terminal && !string.IsNullOrWhiteSpace(current.Id))
        {
            await Task.Delay(PollDelay, timeout.Token);
            current = await _ubag.GetJobAsync(current.Id, timeout.Token);
        }
        return current;
    }

    /// <summary>
    /// Deterministic idempotency key over (tenant, report, fileName, size) so a
    /// retried upload of the same audio reuses the same gateway job. The key is
    /// a single path-safe token (no slash, backslash, or percent).
    /// </summary>
    private static string BuildIdempotencyKey(Guid tenantId, Guid reportId, string fileName, long sizeBytes)
    {
        var material = $"{tenantId:N}|{reportId:N}|{fileName}|{sizeBytes}";
        return $"radiopad-transcribe-{Sha256(material)[..32]}";
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""))).ToLowerInvariant();
}
