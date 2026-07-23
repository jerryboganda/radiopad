using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Providers.Local;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Whole-report generation against the on-device MedGemma model, entirely on this workstation.
///
/// <para>The normal <c>/api/reports/{id}/generate</c> path always runs through the hosted API — fine
/// for cloud providers, but wrong for an on-device model: the radiologist's whole point in picking
/// MedGemma is that nothing leaves the workstation, and a hosted container has neither the GGUF file
/// nor a running llama-server on its own loopback. This endpoint gives the desktop frontend a way to
/// run generation locally end to end when an on-device provider is selected, calling
/// <see cref="LlamaCppProvider"/> directly (bypassing <see cref="IAiGateway"/> — no tenant/PHI-policy
/// registry lookup is needed, since there is only ever one possible target: the model on this
/// machine).</para>
///
/// <para>Mirrors <see cref="LocalModelsController"/>'s safety model: not report/tenant-scoped, so it is
/// safe to serve anonymously — the desktop sidecar binds 127.0.0.1 only — and gated on
/// <see cref="LocalSttModels.IsEnabled"/> so a hosted build stays inert (whitelisted in
/// RadioPadBearerMiddleware so that inert result is a clean response, not a 401).</para>
///
/// <para><b>Own prompt, not <see cref="ReportingService.BuildStructuredPrompt"/> (deliberate).</b> That
/// prompt is tuned for and validated against large cloud models (see
/// ConsultantGradeGeneratePromptTests) and asks for heading/bullet-formatted findings purely through
/// prose instructions. Empirically that does NOT work on MedGemma 1.5 4B (Q4_K_M): tested directly
/// against the local llama-server with and without the GBNF grammar, it produced a single
/// undifferentiated prose paragraph either way — a small-model capability gap, not a bug in the
/// grammar or the prompt's content. A short instruction plus a worked example reliably gets it to
/// reproduce the heading+bullet layout instead (verified the same way) — but the example MUST use
/// bracketed placeholders, not concrete clinical values: an earlier version with concrete example
/// values (a specific measurement and organ) fixed the formatting but caused the model to sometimes
/// copy those exact fabricated values into an unrelated case (a phantom kidney stone in a chest CT
/// that never mentioned kidneys) — a hallucination risk that placeholder-only examples eliminated
/// in testing without losing the formatting improvement. A long, exhaustive systematic review can
/// also make a 4B model degenerate into repeating its last line until it exhausts the token budget
/// once it runs out of new content — <see cref="AiCompletionRequest.RepeatPenalty"/> fixes that; a
/// strong penalty (1.3) was tested first and rejected because it also pushed the model to fabricate
/// a different measurement rather than repeat the dictated one verbatim, a moderate one (1.1) did
/// not.</para>
///
/// <para><b>Rulebook scope (v1):</b> this path does not resolve a tenant's rulebook — the frontend has
/// no way to hand it one without a network round trip, which would reintroduce the exact "internet
/// involvement" this endpoint exists to avoid. Tenant-specific rulebook prompt-block overrides are a
/// possible fast-follow, not a v1 requirement.</para>
/// </summary>
[ApiController]
[Route("api/local-generation")]
[AllowAnonymous]
public sealed class LocalGenerationController : ControllerBase
{
    private readonly IReadOnlyList<IAiProviderAdapter> _adapters;
    private readonly AiJobRegistry _registry;
    private readonly LocalGenerationJobRunner _runner;
    private readonly ILogger<LocalGenerationController> _log;
    // Optional so the existing direct-construction unit tests (LocalGenerationControllerTests) keep
    // compiling unchanged; DI always supplies both (they are always registered). Used only by the
    // /events SSE loop — ApplicationStopping ends the loop on graceful shutdown, config carries the
    // keep-alive interval (shared AiJobs:SseKeepAliveSeconds, default 15).
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly IConfiguration? _config;

    public LocalGenerationController(
        IEnumerable<IAiProviderAdapter> adapters,
        AiJobRegistry registry,
        LocalGenerationJobRunner runner,
        ILogger<LocalGenerationController> log,
        IHostApplicationLifetime? lifetime = null,
        IConfiguration? config = null)
    {
        _adapters = adapters.ToList();
        _registry = registry;
        _runner = runner;
        _log = log;
        _lifetime = lifetime;
        _config = config;
    }

    // Registry identity for this tenant-less, single-user, loopback-only path: there is no tenant
    // and no user, so both keys are Guid.Empty. The job's ReportId carries the client-supplied
    // correlation id (the HOSTED report's Guid) instead — see GenerateReportJobDto.
    private const string JobKind = "local-generate";
    private const string JobMode = "report";

    public record GenerateReportDto(
        string? Modality, string? BodyPart, string? Contrast, int? Age, string? Gender,
        string? Indication, string? Findings);

    /// <summary>
    /// Submit body for the async job endpoints: the same fields as <see cref="GenerateReportDto"/>
    /// plus <c>CorrelationId</c> — the HOSTED report's Guid, supplied by the desktop frontend and
    /// opaque to the sidecar (which has no reports DB of its own). It becomes the job's ReportId so
    /// the top-right jobs widget can navigate to the right hosted report on completion, and the
    /// single-flight key so a re-submit for the same report returns the in-flight job rather than
    /// stacking a second generation onto the single-request llama-server.
    /// </summary>
    public record GenerateReportJobDto(
        string? Modality, string? BodyPart, string? Contrast, int? Age, string? Gender,
        string? Indication, string? Findings, Guid CorrelationId)
    {
        public GenerateReportDto ToReportDto() =>
            new(Modality, BodyPart, Contrast, Age, Gender, Indication, Findings);
    }

    public record GeneratedReportSections(
        string Indication, string Technique, string Findings, string Impression, string Recommendations,
        string Provider, string Model, int LatencyMs);

    private const string SystemPrompt =
        "You are a radiologist's report-writing assistant. You write radiology report FINDINGS as a " +
        "list of organ/system headings, each followed by short bullet statements. You NEVER write " +
        "findings as flowing prose paragraphs. Preserve every dictated measurement, laterality, and " +
        "negation exactly; never invent a finding that was not dictated or implied by the systematic " +
        "review of normal anatomy.";

    /// <summary>
    /// Formatting rule stated FIRST and reinforced with a worked example — proven far more effective on
    /// a small model than the equivalent instruction expressed only as abstract prose (see class
    /// remarks). The example uses bracketed PLACEHOLDERS instead of concrete clinical values
    /// deliberately: an earlier version with concrete example values (8 mm calculus, 12 mm gallstone)
    /// measurably fixed the formatting, but the model would sometimes copy those exact fabricated
    /// numbers into an unrelated case's findings (e.g. inventing a phantom kidney stone in a chest CT
    /// report that never mentioned kidneys) — a hallucination risk, not just a cosmetic one. Removing
    /// every concrete, plausible-looking value from the example eliminated that leakage in testing
    /// while still teaching the layout, including the paired-organ-per-side split.
    /// </summary>
    private const string InstructionsTemplate = """
        Write the findings as a list of organ/system headings, each followed by short bullet statements.
        Follow this EXACT layout — the example below shows the LAYOUT ONLY, using bracketed placeholders
        instead of real content, because it is not part of this case:

        EXAMPLE LAYOUT (placeholders in brackets — never copy an organ name, number, or finding from this
        example into your answer; only copy the layout style):
        <ORGAN NAME>:
        • <one short finding for this organ>
        • <a pertinent negative for this organ, if relevant>

        <PAIRED ORGAN> RIGHT:
        • <finding specific to the right side>

        <PAIRED ORGAN> LEFT:
        • <finding specific to the left side, described separately because it differs from the right>

        Rules:
        - One heading per organ/system, in UPPERCASE, alone on its own line, ending with a colon.
        - Each bullet starts with "• " and stays on one line.
        - Leave one blank line between each organ/system group.
        - ONLY include organs and systems that this study's modality/body part actually images. Do not
          mention an organ from the example layout above unless this real case also needs it.
        - When a paired organ (kidneys, lungs, adrenal glands, etc.) has a dictated finding on only one
          side, give it its OWN heading per side (e.g. "RIGHT KIDNEY:" / "LEFT KIDNEY:") and describe the
          actual finding under the affected side — never summarise a paired organ as fully normal
          "bilaterally" when one side has a dictated finding.
        - Cover every organ routinely assessed on THIS study, not only the ones with dictated findings —
          but never an organ outside the field of view of this study's modality and body part.
        - Never write a paragraph of connected sentences. Every line is either a heading or a bullet.

        Give the impression as short numbered lines ("1. ...", "2. ...") synthesising the findings by
        clinical significance. Give recommendations as one or two short sentences, or state that no
        specific follow-up is indicated when none is warranted.
        """;


    /// <summary>
    /// Kept as its own plain (non-interpolated) raw string so its literal <c>{</c>/<c>}</c>
    /// characters never collide with <see cref="BuildUserPrompt"/>'s single-brace
    /// interpolation holes — a <c>$"""..."""</c> raw string with one <c>$</c> treats
    /// every single <c>{</c> as the start of an interpolation, so a literal JSON
    /// brace inline there fails to compile (CS9006). Interpolated into the prompt
    /// as a single <c>{JsonSchemaExample}</c> hole instead.
    /// </summary>
    private const string JsonSchemaExample = """
        {
          "indication": "",
          "technique": "",
          "findings": "",
          "impression": "",
          "recommendations": ""
        }
        """;

    private static string BuildUserPrompt(GenerateReportDto dto)
    {
        var age = dto.Age is { } a ? a.ToString() : "Unknown";
        var gender = string.IsNullOrWhiteSpace(dto.Gender) ? "Unknown" : dto.Gender;
        var contrast = string.IsNullOrWhiteSpace(dto.Contrast) ? "Unspecified" : dto.Contrast;
        return $"""
            Modality: {dto.Modality}
            Body part: {dto.BodyPart}
            Contrast: {contrast}
            Patient: age {age}, gender {gender}

            CLINICAL HISTORY / INDICATION:
            {dto.Indication}

            POSITIVE FINDINGS (dictated):
            {dto.Findings}

            INSTRUCTION:
            {InstructionsTemplate}

            Respond with a single JSON object exactly matching this schema:
            {JsonSchemaExample}
            Return the raw JSON object only. Escape every line break inside a string value as \n; never
            emit a raw newline inside a quoted string.
            """;
    }

    /// <summary>
    /// The on-device provider config is fixed — there is only ever one target (the MedGemma model on
    /// this workstation), so no tenant/registry lookup. Shared by the sync endpoint and the async job
    /// runner so both paths build the byte-identical request (prompt, GBNF grammar, repeat penalty).
    /// </summary>
    internal static AiCompletionRequest BuildCompletionRequest(GenerateReportDto dto, IProgress<AiStreamChunk>? onStream = null)
    {
        var provider = new ProviderConfig
        {
            Name = "local-medgemma",
            Adapter = LlamaCppProvider.AdapterId,
            Model = LocalModelCatalog.MedGemmaId,
            EndpointUrl = LlamaServerProcess.BaseUrl,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        };

        return new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: SystemPrompt,
            UserPrompt: BuildUserPrompt(dto),
            PromptVersion: "local-generate-v2-fewshot",
            ContainsPhi: true)
        {
            Grammar = DictationGrammar.ReportSectionsGbnf,
            // Empirically tuned against the real model — see class remarks for what was tried and why.
            RepeatPenalty = 1.1,
            RepeatLastN = 256,
            // AI-013 — when the async job runner passes a sink, LlamaCppProvider streams the /completion
            // response and reports each token chunk here (grammar/stop/repeat_penalty are all honoured
            // server-side during streaming). The sync /report endpoint passes null → unchanged behaviour.
            OnStream = onStream,
        };
    }

    /// <summary>
    /// Post-process a raw model result into the section-shaped response — shared by the sync endpoint
    /// (returned in the HTTP body) and the async job runner (handed to the registry as the poll
    /// payload, so the frontend's origin-agnostic poller sees the same shape from either path).
    /// </summary>
    internal static GeneratedReportSections BuildSections(AiResult result)
    {
        var sections = ReportSectionJson.Parse(result.Text);
        return new GeneratedReportSections(
            Indication: sections.GetValueOrDefault("indication", string.Empty),
            Technique: sections.GetValueOrDefault("technique", string.Empty),
            Findings: ReportingService.FormatGeneratedFindings(sections.GetValueOrDefault("findings", string.Empty)),
            Impression: sections.GetValueOrDefault("impression", string.Empty),
            Recommendations: sections.GetValueOrDefault("recommendations", string.Empty),
            Provider: result.Provider,
            Model: result.Model,
            LatencyMs: result.LatencyMs);
    }

    /// <summary>
    /// Synchronous whole-report generation — holds the HTTP request open for the entire model run
    /// (cold-start model load up to ~3 min, then CPU-bound generation for minutes).
    ///
    /// <para><b>Deprecated in favour of the <c>/jobs</c> endpoints below</b>, which decouple the run
    /// from the connection so a dropped webview fetch no longer aborts generation. Kept working,
    /// unchanged, for one release because older desktop builds still call it; remove once no shipped
    /// build depends on it.</para>
    /// </summary>
    [HttpPost("report")]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportDto dto, CancellationToken ct)
    {
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device generation is only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        var llama = _adapters.FirstOrDefault(a => a.Id == LlamaCppProvider.AdapterId);
        if (llama is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The on-device AI adapter is not registered.", kind = "adapter_unavailable" });

        AiResult result;
        try
        {
            result = await llama.CompleteAsync(BuildCompletionRequest(dto), ct);
        }
        catch (ProviderTransportException ex)
        {
            _log.LogWarning(ex, "On-device report generation failed to reach the local llama-server.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message, kind = "provider_transport" });
        }

        return Ok(BuildSections(result));
    }

    // ── Async job endpoints ─────────────────────────────────────────────────────────────────────
    // Decouple generation from the HTTP connection: submit returns a job id immediately, the client
    // polls with fast requests, and a dropped webview fetch no longer aborts a minutes-long run. The
    // registry is in-memory ONLY here (the sidecar's SQLite is throwaway by doctrine, and a sidecar
    // restart kills the llama-server child anyway) — the same envelope keys as the hosted poll
    // endpoint (ReportsController.AiJobStatus) so the frontend poller is origin-agnostic, plus a
    // computed live `stage`.

    private IActionResult? SttGate() =>
        LocalSttModels.IsEnabled()
            ? null
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device generation is only available in the RadioPad desktop app.", kind = "stt_unavailable" });

    /// <summary>
    /// Submit an on-device generation job. Returns <c>202 { jobId, status }</c> immediately; the
    /// actual run is handed to <see cref="LocalGenerationJobRunner"/>, which serialises it behind the
    /// single-request llama-server. Re-submitting for the same correlation id (hosted report) while a
    /// job is still running returns that in-flight job rather than stacking a second generation.
    /// </summary>
    [HttpPost("jobs")]
    public IActionResult SubmitJob([FromBody] GenerateReportJobDto dto)
    {
        if (SttGate() is { } gate) return gate;

        if (dto.CorrelationId == Guid.Empty)
            return BadRequest(new { error = "correlationId is required.", kind = "correlation_required" });

        var llama = _adapters.FirstOrDefault(a => a.Id == LlamaCppProvider.AdapterId);
        if (llama is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The on-device AI adapter is not registered.", kind = "adapter_unavailable" });

        // Single-flight on the correlation id (used as ReportId). A running job for the same hosted
        // report is returned instead of launching a duplicate — mirrors the hosted submit endpoints.
        if (_registry.TryGetRunning(Guid.Empty, dto.CorrelationId, JobKind, JobMode, out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        var job = _registry.Create(Guid.Empty, dto.CorrelationId, Guid.Empty, JobKind, JobMode);
        // Fire-and-forget: RunAsync runs its synchronous prefix (stage → "queued", CTS registered)
        // on this thread before yielding, so a poll immediately after this 202 already sees the job.
        _ = _runner.RunAsync(job.Id, dto.ToReportDto());
        return Accepted(new { jobId = job.Id, status = job.Status });
    }

    /// <summary>
    /// Poll a job. Same envelope keys as the hosted <c>ReportsController.AiJobStatus</c> so the
    /// frontend poller is origin-agnostic, PLUS <c>stage</c> ("queued" | "model-loading" |
    /// "generating"), computed live from the runner (never stored on the registry record).
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    public IActionResult JobStatus(Guid jobId)
    {
        if (SttGate() is { } gate) return gate;

        if (!_registry.TryGet(jobId, out var job) || job.Kind != JobKind)
            return NotFound(new
            {
                error = "On-device generation job not found — the sidecar may have restarted mid-generation. Please try again.",
                kind = "job_not_found",
            });

        // Live streamed progress for a running job. This path is loopback-only (127.0.0.1 bind + the
        // bearer-middleware whitelist), so PHI never leaves the workstation — including the raw partial
        // model text in the poll body is safe and lets the desktop preview render live even without the
        // SSE stream. Omitted (WhenWritingNull) once terminal. Tokens only; percent is always null (a
        // fake bar — see BuildCompletionRequest / design §3.10), so it is not exposed.
        var progress = job.Status == "running" ? _registry.ProgressOf(jobId) : null;

        return Ok(new
        {
            jobId = job.Id,
            kind = job.Kind,
            mode = job.Mode,
            status = job.Status,
            elapsedMs = (long)((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.CreatedAt).TotalMilliseconds,
            result = job.Status == "ok" ? job.Payload : null,
            error = job.Error,
            errorKind = job.ErrorKind,
            stage = StageOf(job),
            progress = progress is { } p ? new { tokens = p.Tokens } : null,
            partial = progress?.PartialText,
        });
    }

    /// <summary>
    /// List this sidecar's jobs, newest/running first — lets the desktop widget rehydrate after an app
    /// restart. Per-item shape mirrors the poll endpoint minus <c>result</c> (kept light); the
    /// correlation id travels as <c>reportId</c> so the widget can open the right hosted report.
    /// </summary>
    [HttpGet("jobs")]
    public IActionResult ListJobs()
    {
        if (SttGate() is { } gate) return gate;

        var jobs = _registry.ListForUser(Guid.Empty, Guid.Empty);
        // Wrapped in { jobs } to match the hosted JobsController.List envelope —
        // the frontend's api.localGenerate.listJobs() types and destructures this
        // shape identically for both origins (a bare array here silently no-ops
        // the sidecar rehydration path instead of throwing).
        return Ok(new
        {
            jobs = jobs.Select(job => new
            {
                jobId = job.Id,
                kind = job.Kind,
                mode = job.Mode,
                status = job.Status,
                elapsedMs = (long)((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.CreatedAt).TotalMilliseconds,
                error = job.Error,
                errorKind = job.ErrorKind,
                stage = StageOf(job),
                reportId = job.ReportId,
            }),
        });
    }

    /// <summary>
    /// Request cancellation of a job. Best-effort: the runner's per-job CTS aborts the outbound
    /// <c>/completion</c> call, and llama.cpp cancels the generation slot on client disconnect in
    /// current builds — but a full stop can take up to ~a minute mid-generation. Idempotent for a job
    /// that has already reached a terminal status; 404 for an unknown job.
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/cancel")]
    public IActionResult CancelJob(Guid jobId)
    {
        if (SttGate() is { } gate) return gate;

        if (!_registry.TryGet(jobId, out var job) || job.Kind != JobKind)
            return NotFound(new
            {
                error = "On-device generation job not found.",
                kind = "job_not_found",
            });

        // Registry status is "running" for both a queued (waiting on the semaphore) and an actively
        // generating job; the runner tells them apart via the stage. A queued job cancelled here
        // never reaches the provider — its semaphore wait observes the cancellation and goes straight
        // to "cancelled".
        if (job.Status != "running")
            return Ok(new { jobId = job.Id, status = job.Status, cancelRequested = false });

        _registry.TryRequestCancel(jobId);
        return Accepted(new { jobId = job.Id, status = "running", cancelRequested = true });
    }

    /// <summary>
    /// Single loopback-only Server-Sent-Events stream over ALL of this sidecar's on-device generation
    /// jobs (there is only ever the tenant-less <c>Guid.Empty</c> working set). Matches the desktop's
    /// one-SSE-manager-per-origin model — the frontend opens ONE stream for the sidecar, not one per
    /// job. No event bus is needed on this path: it is a registry-poll loop (100 ms) that, for each
    /// active job, emits <c>progress</c> (on token-count change) and <c>partial</c> (the newly streamed
    /// text) events for the delta since the last send, a poll-envelope-shaped <c>job</c> event
    /// (<c>{jobId,status,error,errorKind,stage}</c>) once a job goes terminal or vanishes from the
    /// registry, and a <c>: keep-alive</c> comment when idle. PHI stays on-device (127.0.0.1 bind + the
    /// RadioPadBearerMiddleware whitelist), so streaming raw partial model output here is safe.
    ///
    /// <para>Gated exactly like the sibling job endpoints — a hosted build stays inert (503
    /// <c>stt_unavailable</c>). Returns <see cref="Task"/> and writes the SSE body manually with the
    /// same hygiene as <see cref="EventsController"/> (via the shared <see cref="SseWriter"/>: headers +
    /// DisableBuffering, a linked CTS on RequestAborted + ApplicationStopping, manual camelCase +
    /// omit-null JSON).</para>
    /// </summary>
    [HttpGet("events")]
    public async Task Events(CancellationToken ct)
    {
        // Gate like the siblings, but inline — this action returns Task and owns the response body, so
        // it cannot return SttGate()'s IActionResult. Must run BEFORE any SSE header/body is written.
        if (!LocalSttModels.IsEnabled())
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(
                new { error = "On-device generation is only available in the RadioPad desktop app.", kind = "stt_unavailable" }, ct);
            return;
        }

        SseWriter.PrepareResponse(Response);

        var keepAlive = TimeSpan.FromSeconds(Math.Max(1, _config?.GetValue<int?>("AiJobs:SseKeepAliveSeconds") ?? 15));
        var pollInterval = TimeSpan.FromMilliseconds(100);

        // Client abort AND graceful shutdown both end the loop; on ApplicationStopping the connection
        // closes cleanly so SSE never holds Kestrel shutdown hostage.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted, _lifetime?.ApplicationStopping ?? CancellationToken.None, ct);
        var token = linked.Token;

        // Per-job send cursors: the token count and partial length already emitted, so each poll sends
        // only the delta. `terminated` remembers jobs whose terminal `job` event was already sent (so it
        // fires exactly once); `seen` remembers every job observed so one that later vanishes from the
        // registry (evicted before its terminal snapshot was caught) still owes a terminal `job` event.
        var lastTokens = new Dictionary<Guid, int>();
        var lastPartialLen = new Dictionary<Guid, int>();
        var terminated = new HashSet<Guid>();
        var seen = new HashSet<Guid>();
        var lastActivity = DateTimeOffset.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var wrote = false;
                var jobs = _registry.ListForUser(Guid.Empty, Guid.Empty);
                var live = new HashSet<Guid>();

                foreach (var job in jobs)
                {
                    if (job.Kind != JobKind) continue;
                    live.Add(job.Id);
                    seen.Add(job.Id);

                    if (job.Status == "running")
                    {
                        var prog = _registry.ProgressOf(job.Id);
                        if (prog is null) continue;

                        if (prog.Tokens != lastTokens.GetValueOrDefault(job.Id))
                        {
                            lastTokens[job.Id] = prog.Tokens;
                            await SseWriter.WriteEventAsync(Response, "progress",
                                new { jobId = job.Id, tokens = prog.Tokens }, token);
                            wrote = true;
                        }

                        var full = prog.PartialText ?? "";
                        var prevLen = lastPartialLen.GetValueOrDefault(job.Id);
                        // A shorter buffer than last time = the registry's failover-reset cleared it
                        // (never happens on-device — a single provider — but stay correct): resend whole.
                        if (full.Length < prevLen) prevLen = 0;
                        if (full.Length > prevLen)
                        {
                            lastPartialLen[job.Id] = full.Length;
                            await SseWriter.WriteEventAsync(Response, "partial",
                                new { jobId = job.Id, delta = full[prevLen..] }, token);
                            wrote = true;
                        }
                    }
                    else if (terminated.Add(job.Id))
                    {
                        await SseWriter.WriteEventAsync(Response, "job", new
                        {
                            jobId = job.Id,
                            status = job.Status,
                            error = job.Error,
                            errorKind = job.ErrorKind,
                            stage = StageOf(job),
                        }, token);
                        wrote = true;
                        lastTokens.Remove(job.Id);
                        lastPartialLen.Remove(job.Id);
                    }
                }

                // A job seen active earlier that has vanished from the registry (evicted before its
                // terminal snapshot was observed) still owes the client a terminal `job` event so its
                // reducer can close it out.
                foreach (var goneId in seen)
                {
                    if (live.Contains(goneId) || !terminated.Add(goneId)) continue;
                    await SseWriter.WriteEventAsync(Response, "job", new
                    {
                        jobId = goneId,
                        status = "error",
                        error = "The on-device generation job is no longer available.",
                        errorKind = "job_not_found",
                    }, token);
                    wrote = true;
                    lastTokens.Remove(goneId);
                    lastPartialLen.Remove(goneId);
                }

                var now = DateTimeOffset.UtcNow;
                if (wrote)
                {
                    lastActivity = now;
                }
                else if (now - lastActivity >= keepAlive)
                {
                    await SseWriter.WriteKeepAliveAsync(Response, token);
                    lastActivity = now;
                }

                await Task.Delay(pollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Client aborted or the app is stopping — clean close.
        }
        catch (Exception)
        {
            // Any write failure means the client is gone; exit.
        }
    }

    /// <summary>Live stage for a job: the runner's tracked stage while active, else null once terminal
    /// (the runner drops the entry). Falls back to "queued" for the brief window before the runner's
    /// synchronous prefix records a stage.</summary>
    private string? StageOf(AiJobRegistry.AiJobState job) =>
        _runner.StageOf(job.Id) ?? (job.Status == "running" ? LocalGenerationJobRunner.StageQueued : null);
}
