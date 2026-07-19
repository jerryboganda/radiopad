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

    public LocalMedGemmaFormatter(IEnumerable<IAiProviderAdapter> adapters)
    {
        _llama = adapters.First(a => a.Id == LlamaCppProvider.AdapterId);
    }

    public bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnabledEnv));

    public async Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct)
    {
        var endpoint = Environment.GetEnvironmentVariable(UrlEnv);
        endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultUrl : endpoint.Trim();
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
