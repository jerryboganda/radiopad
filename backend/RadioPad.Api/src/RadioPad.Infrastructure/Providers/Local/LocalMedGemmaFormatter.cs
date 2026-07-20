using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Dictation brief §2.2 — the OPTIONAL on-device report formatter. Runs the deterministically
/// protected transcript through MedGemma 1.5 4B (Q4_K_M) on a llama-server via the
/// <see cref="LlamaCppProvider"/> (LocalOnly), constrained by the §5.4 GBNF grammar at temperature 0.
/// PHI never leaves the device: the endpoint is enforced to be loopback. Enabled per-workstation by
/// <c>RADIOPAD_LOCAL_FORMATTER_ENABLED</c> (set by the desktop sidecar); inert everywhere else, so
/// the cloud formatter stays the default.
///
/// <para><b>The llama-server is provisioned and supervised by RadioPad</b> (resolved 2026-07; see
/// IMPLEMENTATION_NOTES.md). <see cref="SttModelProvisioner.EnsureMedGemmaWithRuntimeAsync"/> fetches
/// the pinned llama.cpp runtime (<see cref="LocalRuntimes.LlamaServerId"/>) alongside the GGUF, and
/// <see cref="LlamaServerProcess"/> starts it lazily on first use — the operator does not run it
/// themselves. An explicit <c>RADIOPAD_LOCAL_LLAMA_URL</c> still wins, for the operator who wants to
/// point at their own server.</para>
///
/// <para>A transport failure is nonetheless surfaced as an actionable message rather than a bare
/// "connection refused": the model download alone is ~2.5 GB, and someone who paid that deserves to
/// be told what is actually missing (runtime absent, server failed to start, or still loading).</para>
/// </summary>
public sealed class LocalMedGemmaFormatter : ILocalReportFormatter
{
    public const string EnabledEnv = "RADIOPAD_LOCAL_FORMATTER_ENABLED";
    public const string DefaultEnv = "RADIOPAD_LOCAL_FORMATTER_DEFAULT";
    public const string UrlEnv = "RADIOPAD_LOCAL_LLAMA_URL";
    private const string DefaultUrl = "http://127.0.0.1:8080";
    private const string PromptVersion = "v1.dictation.medgemma";

    private const string SystemPrompt =
        "You are RadioPad's report formatter. You convert a radiologist's dictated findings into a " +
        "structured radiology report. Use ONLY information present in the dictation — add nothing. " +
        "Reproduce every number, measurement, laterality (left/right) and date EXACTLY as provided. " +
        "Do not invent, infer, or complete any finding, differential, or recommendation not dictated. " +
        "Do not interpret or describe any image. Output the report JSON object only.";

    private readonly IAiProviderAdapter _llama;
    private readonly LlamaServerProcess? _server;
    private readonly IHttpClientFactory? _http;

    public LocalMedGemmaFormatter(
        IEnumerable<IAiProviderAdapter> adapters,
        LlamaServerProcess? server = null,
        IHttpClientFactory? http = null)
    {
        _llama = adapters.First(a => a.Id == LlamaCppProvider.AdapterId);
        _server = server;
        _http = http;
    }

    /// <summary>Absolute path of the provisioned MedGemma GGUF, or null when it is not downloaded.</summary>
    private static string? ResolveModelPath()
    {
        var dir = LocalSttModels.ResolveModelDir(LocalModelCatalog.MedGemmaId);
        if (dir is null) return null;
        var path = Path.Combine(dir, LocalModelCatalog.MedGemmaFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Resolve the endpoint to format against, starting the managed llama-server if needed.
    ///
    /// <para>An explicit <c>RADIOPAD_LOCAL_LLAMA_URL</c> always wins and is used verbatim: an
    /// operator running their own server (different port, different model, a shared box) must not
    /// have a second one started underneath them. Only when no URL is configured do we take
    /// responsibility for the process.</para>
    /// </summary>
    private async Task<string> ResolveEndpointAsync(CancellationToken ct)
    {
        var configured = Environment.GetEnvironmentVariable(UrlEnv);
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        if (_server is null || _http is null) return DefaultUrl;

        var modelPath = ResolveModelPath();
        if (modelPath is null) return DefaultUrl; // not downloaded — fall through to the actionable error

        var baseUrl = await _server.EnsureRunningAsync(modelPath, ct);
        if (baseUrl is null) return DefaultUrl;

        // Loading a multi-GB GGUF on CPU legitimately takes tens of seconds on a cold start; the
        // first refused connection is not a failure. "ai-local" (not "ai") so this never shares
        // the cloud-tuned attempt timeout or circuit breaker.
        await _server.WaitUntilHealthyAsync(_http.CreateClient("ai-local"), TimeSpan.FromMinutes(3), ct);
        return baseUrl;
    }

    public bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnabledEnv));

    /// <summary>
    /// Opt-in, separate from <see cref="Available"/>: the desktop sidecar enables the CAPABILITY so
    /// the on-device endpoint works, while cloud stays the default report formatter (decision D1).
    /// Set <c>RADIOPAD_LOCAL_FORMATTER_DEFAULT</c> to route report-scoped drafting here too.
    /// </summary>
    public bool PreferredForReportDrafts =>
        Available && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultEnv));

    public async Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct)
    {
        var endpoint = await ResolveEndpointAsync(ct);
        EnsureLoopback(endpoint); // PHI must never leave the device

        var provider = new ProviderConfig
        {
            Name = "local-medgemma",
            Adapter = LlamaCppProvider.AdapterId,
            Model = LocalModelCatalog.MedGemmaId,
            EndpointUrl = endpoint,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        };

        var request = new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: SystemPrompt,
            UserPrompt: BuildUserPrompt(protectedTranscript, context),
            PromptVersion: PromptVersion,
            ContainsPhi: true)
        {
            Temperature = 0.0,                                              // deterministic formatting
            Grammar = context.Grammar ?? DictationGrammar.ReportSectionsGbnf, // §5.4 structural constraint
        };

        AiResult result;
        try
        {
            result = await _llama.CompleteAsync(request, ct);
        }
        catch (ProviderTransportException ex)
        {
            // Nothing is listening on loopback. RadioPad provisions and starts the server itself, so
            // name the link in that chain that actually broke rather than telling the radiologist to
            // go install llama.cpp — which they have not needed to do since the runtime became
            // auto-provisioned, and which sends them chasing a problem they do not have.
            throw new ProviderTransportException(
                $"The on-device MedGemma formatter could not reach its llama-server at {endpoint}. " +
                DiagnoseUnreachable() +
                $" Cloud formatting still works in the meantime. Underlying error: {ex.Message}",
                inner: ex);
        }
        var sections = ReportSectionJson.Parse(result.Text);

        var dict = new Dictionary<string, string>
        {
            ["indication"] = sections.GetValueOrDefault("indication", string.Empty),
            ["technique"] = sections.GetValueOrDefault("technique", string.Empty),
            ["findings"] = sections.GetValueOrDefault("findings", string.Empty),
            ["impression"] = sections.GetValueOrDefault("impression", string.Empty),
            ["recommendations"] = sections.GetValueOrDefault("recommendations", string.Empty),
        };

        return new FormatterOutput(dict, result.Provider, result.Model, result.LatencyMs);
    }

    private static string BuildUserPrompt(string transcript, DictationFormatContext ctx) => $"""
        Modality: {ctx.Modality}
        Body part: {ctx.BodyPart}
        Indication: {ctx.Indication}

        DICTATION:
        {transcript}

        Format the dictation into a structured report. Respond with a single JSON object with keys
        indication, technique, findings, impression, recommendations (use empty strings for sections
        not dictated).
        """;

    /// <summary>
    /// Work out which link in the offline-formatting chain is missing, so the message names a
    /// cause the user can act on. Ordered from the most likely and most fixable outward: model
    /// absent → runtime absent → server present but not answering.
    /// </summary>
    private string DiagnoseUnreachable()
    {
        if (ResolveModelPath() is null)
            return "The MedGemma model is not downloaded — download it from On-device models.";

        var runtimeDir = LocalRuntimes.ResolveRuntimeDir(LocalRuntimes.LlamaServerId);
        if (!LocalRuntimes.IsLlamaServerInstalled(runtimeDir))
            return "The llama.cpp runtime that normally arrives with the model is missing — "
                + "re-download MedGemma from On-device models to fetch it.";

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlEnv)))
            return $"A custom endpoint is configured via {UrlEnv}; RadioPad did not start a server "
                + "of its own. Check that yours is running, or clear that variable to let RadioPad manage it.";

        return _server is null
            ? "No managed llama-server is available in this build."
            : "The model and runtime are both installed, so the server failed to start or is still "
                + "loading — a first cold start of a 2.5 GB model can take a few minutes.";
    }

    private static void EnsureLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Local MedGemma endpoint '{url}' is not a valid URL.");

        var host = uri.Host;
        var isLoopback = uri.IsLoopback
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1";
        if (!isLoopback)
            throw new InvalidOperationException(
                $"Local MedGemma endpoint must be loopback (PHI must not leave the device); got host '{host}'.");
    }
}
