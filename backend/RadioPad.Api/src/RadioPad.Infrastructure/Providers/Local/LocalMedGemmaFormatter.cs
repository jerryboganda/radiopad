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
/// <para><b>The llama-server is NOT bundled yet.</b> This adapter speaks HTTP to one that is already
/// listening on loopback — today the operator runs it themselves (llama.cpp's <c>llama-server</c>
/// pointed at the provisioned GGUF). Shipping that binary is tracked in IMPLEMENTATION_NOTES.md.
/// Until then a transport failure is surfaced as an actionable message rather than a bare
/// "connection refused": the model download alone is ~2.5 GB, and someone who paid that deserves to
/// be told what is actually missing.</para>
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
        // first refused connection is not a failure.
        await _server.WaitUntilHealthyAsync(_http.CreateClient("ai"), TimeSpan.FromMinutes(3), ct);
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
            // Nothing is listening on loopback — almost always "the llama-server isn't running",
            // not a bug. Say so, because the raw transport error ("connection refused") gives the
            // radiologist no idea that a separate process is required.
            throw new ProviderTransportException(
                $"The on-device MedGemma formatter could not reach a llama-server at {endpoint}. " +
                "Start llama.cpp's llama-server pointed at the downloaded MedGemma GGUF, or turn the " +
                "offline formatter off to use the cloud formatter (the default). " +
                $"Underlying error: {ex.Message}",
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
