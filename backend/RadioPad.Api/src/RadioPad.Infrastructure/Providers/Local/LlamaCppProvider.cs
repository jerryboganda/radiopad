using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Iter-32 AI-011 — llama.cpp HTTP server adapter. Targets
/// <c>POST {base}/completion</c> which is the canonical endpoint for the
/// <c>llama-server</c> binary that ships with llama.cpp. Default endpoint
/// is <c>http://127.0.0.1:8080</c>; compliance class defaults to
/// <see cref="ProviderComplianceClass.LocalOnly"/>.
///
/// <para>The system + user prompt are concatenated into the
/// <c>prompt</c> field with a <c>SYSTEM:</c> / <c>USER:</c> framing because
/// llama.cpp's <c>/completion</c> is a single-prompt endpoint; callers that
/// need true chat-style messaging should configure the OpenAI-compatible
/// adapter against llama.cpp's <c>/v1/chat/completions</c> route instead.</para>
/// </summary>
public sealed class LlamaCppProvider : IAiProviderAdapter
{
    public const string AdapterId = "llama-cpp";
    public const string DefaultEndpoint = "http://127.0.0.1:8080";
    public const ProviderComplianceClass DefaultComplianceClass = ProviderComplianceClass.LocalOnly;

    private readonly IHttpClientFactory _http;
    private readonly ILogger<LlamaCppProvider> _log;
    private readonly LlamaServerProcess? _server;
    private readonly ILocalModelCatalog? _catalog;

    public LlamaCppProvider(
        IHttpClientFactory http,
        ILogger<LlamaCppProvider> log,
        LlamaServerProcess? server = null,
        ILocalModelCatalog? catalog = null)
    {
        _http = http;
        _log = log;
        _server = server;
        _catalog = catalog;
    }

    public string Id => AdapterId;

    public async Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var p = request.Provider;
        var baseUrl = string.IsNullOrWhiteSpace(p.EndpointUrl) ? DefaultEndpoint : p.EndpointUrl.TrimEnd('/');
        var managesEndpoint = _server is not null
            && string.Equals(baseUrl, LlamaServerProcess.BaseUrl, StringComparison.OrdinalIgnoreCase);

        // The managed on-device server is started lazily on first use, not kept running as a
        // background service — every caller that talks to OUR default loopback endpoint must
        // make sure it is actually up first. The self-test path (LocalModelsController) and the
        // dictation formatter (LocalMedGemmaFormatter) both already do this before calling in;
        // the generic AiGateway → ProviderRouter → adapter path that "use for report generation"
        // enables does not, so without this a fresh self-test could pass and the very next
        // report-generation request would still get "Connection refused" against a server nobody
        // ever started. An operator who pointed EndpointUrl at their OWN remote server is
        // responsible for running it themselves, hence the endpoint-equality check.
        if (managesEndpoint && _server is not null && !_server.IsRunning)
        {
            var modelPath = ResolveManagedModelPath(p);
            if (modelPath is not null && File.Exists(modelPath))
            {
                await _server.EnsureRunningAsync(modelPath, cancellationToken);
                await _server.WaitUntilHealthyAsync(_http.CreateClient("ai-local"), TimeSpan.FromMinutes(3), cancellationToken);
            }
        }

        var url = $"{baseUrl}/completion";
        var prompt = $"SYSTEM: {request.SystemPrompt}\n\nUSER: {request.UserPrompt}\n\nASSISTANT:";

        // Brief §5.4 — forward the clamped temperature (callers set ≈0 for MedGemma report
        // formatting) and, when supplied, the GBNF grammar so decoding is structurally constrained.
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["n_predict"] = 1024,
            ["stream"] = false,
            ["temperature"] = request.Temperature,
            // /completion is a raw text-completion endpoint: nothing stops the model once it
            // finishes answering, so without a grammar it happily hallucinates a fresh "SYSTEM:"/
            // "USER:" turn and answers that too, and again, until it burns the full n_predict
            // budget (observed: a one-word self-test reply looping for 1024 tokens / ~90s).
            // These mirror the exact turn markers used in `prompt` above, so generation halts the
            // instant the model tries to fabricate another turn. Harmless alongside a grammar too.
            ["stop"] = new[] { "\nSYSTEM:", "\nUSER:" },
        };
        if (!string.IsNullOrWhiteSpace(request.Grammar))
            body["grammar"] = request.Grammar;
        if (request.RepeatPenalty is { } rp)
            body["repeat_penalty"] = rp;
        if (request.RepeatLastN is { } rln)
            body["repeat_last_n"] = rln;

        // Dedicated client (Program.cs "ai-local"): CPU-bound local inference needs minutes, not
        // the cloud-tuned "ai" client's ~60 s attempt budget, and must not share its circuit breaker.
        var client = _http.CreateClient("ai-local");
        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync(url, body, cancellationToken);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            // A bare "Connection refused" against OUR managed endpoint tells a radiologist nothing
            // actionable — name which link in the auto-start chain above is actually missing, mirroring
            // LocalMedGemmaFormatter.DiagnoseUnreachable for the dictation path. This is the generic
            // report-generation path (AiGateway → ProviderRouter), which had no equivalent until now —
            // see the comment on the auto-start guard above for why that gap existed. An operator's own
            // remote EndpointUrl gets the raw message unchanged: we never tried to manage that server.
            var diagnosis = managesEndpoint ? $" {DiagnoseUnreachable(p)}" : "";
            throw new ProviderTransportException($"{AdapterId}: HTTP transport failure: {hre.Message}.{diagnosis}", inner: hre);
        }
        catch (TaskCanceledException tce) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            throw new ProviderTransportException($"{AdapterId}: request timed out after {sw.ElapsedMilliseconds} ms", inner: tce);
        }

        try
        {
            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await OpenAiChatHelpers.SafeReadAsync(resp, cancellationToken);
                throw new ProviderTransportException(
                    $"{AdapterId}: upstream returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).",
                    statusCode: (int)resp.StatusCode,
                    responseBody: bodyText);
            }

            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            sw.Stop();

            var root = doc.RootElement;
            var text = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            // llama.cpp returns timing as "tokens_evaluated" (prompt) and "tokens_predicted" (completion).
            var pt = root.TryGetProperty("tokens_evaluated", out var te) && te.ValueKind == JsonValueKind.Number ? te.GetInt32() : 0;
            var ctok = root.TryGetProperty("tokens_predicted", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 0;

            return new AiResult(
                Text: text,
                Provider: p.Name,
                Model: string.IsNullOrWhiteSpace(p.Model) ? "llama-cpp" : p.Model,
                LatencyMs: (int)sw.ElapsedMilliseconds,
                InputTokens: pt,
                OutputTokens: ctok,
                PromptVersion: request.PromptVersion);
        }
        finally
        {
            resp.Dispose();
        }
    }

    /// <summary>
    /// Absolute path of the on-device model file <paramref name="p"/> would run against under our
    /// managed model store, or null when it cannot be resolved (unknown model id, or no
    /// local-app-data dir). Existence is deliberately not checked here — callers decide what an
    /// absent file means.
    /// </summary>
    private string? ResolveManagedModelPath(ProviderConfig p)
    {
        var descriptor = _catalog?.ById(p.Model);
        var modelDir = descriptor is null ? null : LocalSttModels.ResolveModelDir(descriptor.Id);
        return modelDir is null || descriptor?.FileName is null
            ? null
            : Path.Combine(modelDir, descriptor.FileName);
    }

    /// <summary>
    /// Work out which link in the auto-start chain is missing, so a "Connection refused" against our
    /// OWN managed endpoint names a cause the caller can act on. Ordered from the most likely and
    /// most fixable outward: model absent → runtime absent → server present but not answering yet.
    /// </summary>
    private string DiagnoseUnreachable(ProviderConfig p)
    {
        var modelPath = ResolveManagedModelPath(p);
        if (modelPath is null || !File.Exists(modelPath))
            return "The model is not downloaded on this workstation — download it from On-device models.";

        var runtimeDir = LocalRuntimes.ResolveRuntimeDir(LocalRuntimes.LlamaServerId);
        if (!LocalRuntimes.IsLlamaServerInstalled(runtimeDir))
            return "The llama.cpp runtime that normally arrives with the model is missing — " +
                "re-download it from On-device models to fetch it.";

        return "The model and runtime are both installed, so the server failed to start or is still " +
            "loading — a first cold start of a multi-GB model can take a few minutes; try again.";
    }

    /// <summary>Probe <c>GET {base}/health</c> (returns <c>{"status":"ok"}</c>).</summary>
    public async Task<(bool ok, string? error)> ProbeAsync(string? endpointUrl, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpoint : endpointUrl.TrimEnd('/');
        try
        {
            var client = _http.CreateClient("ai-local");
            using var resp = await client.GetAsync($"{baseUrl}/health", ct);
            return resp.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
