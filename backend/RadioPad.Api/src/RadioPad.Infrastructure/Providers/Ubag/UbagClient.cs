using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;

namespace RadioPad.Infrastructure.Providers.Ubag;

public sealed class UbagClient : IUbagClient
{
    public const string HttpClientName = "ubag";
    public const string DefaultApiVersion = "2026-05-22";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _http;
    private readonly ILogger<UbagClient> _log;

    public UbagClient(IHttpClientFactory http, ILogger<UbagClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<UbagHealth> GetHealthAsync(CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/v1/health", null, null, ct);
        var root = doc.RootElement;
        var status = ReadString(root, "status") ?? "ok";
        return new UbagHealth(
            Ok: IsOkStatus(status) || ReadBool(root, "ok") == true,
            Status: status,
            Version: ReadString(root, "api_version") ?? ReadString(root, "version"),
            Error: ReadString(root, "error"));
    }

    public async Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/v1/browser/summary", null, null, ct);
        var root = doc.RootElement;
        return new UbagBrowserSummary(
            Instances: ReadInt(root, "instances") ?? ReadInt(root, "instance_count") ?? 0,
            Contexts: ReadInt(root, "contexts") ?? ReadInt(root, "context_count") ?? 0,
            Tabs: ReadInt(root, "tabs") ?? ReadInt(root, "tab_count") ?? 0,
            Status: ReadString(root, "status"),
            RawStatus: root.GetRawText());
    }

    public async Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/v1/targets", null, null, ct);
        var root = doc.RootElement;

        // Real gateway (api-version 2026-05-22) wraps the list in {"data":[...]}.
        // Fall back to a legacy root array, then a legacy {"targets":[...]} envelope.
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : TryProperty(root, "data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array
                ? dataArr
                : TryProperty(root, "targets", out var legacyArr) && legacyArr.ValueKind == JsonValueKind.Array
                    ? legacyArr
                    : default;
        if (array.ValueKind != JsonValueKind.Array) return Array.Empty<UbagTarget>();

        var rows = new List<UbagTarget>();
        foreach (var item in array.EnumerateArray())
        {
            // Real gateway uses "key" as the canonical id; legacy shapes used "id", "target", or "name".
            var id = ReadString(item, "key")
                     ?? ReadString(item, "adapter_key")
                     ?? ReadString(item, "id")
                     ?? ReadString(item, "target")
                     ?? ReadString(item, "name")
                     ?? "unknown";

            var name = ReadString(item, "display_name") ?? ReadString(item, "name") ?? id;

            // Real gateway has no readiness field on /v1/targets — readiness comes from
            // /v1/browser/contexts. If a status/state field is present (legacy) use it;
            // otherwise record "listed" so callers know the target exists but isn't probed.
            var status = ReadString(item, "status") ?? ReadString(item, "state") ?? "listed";

            // "ready" bool wins if present (legacy); otherwise derive from status string only
            // when the status is recognisably-ok, else false (do not assume readiness here).
            var ready = ReadBool(item, "ready") ?? (status != "listed" && IsOkStatus(status));

            rows.Add(new UbagTarget(
                Id: id,
                Name: name,
                Status: status,
                Ready: ready,
                Url: ReadString(item, "url")));
        }
        return rows;
    }

    public async Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/v1/browser/contexts", null, null, ct);
        var root = doc.RootElement;

        // Gateway returns {"data":[...]} envelope; fall back to a root array.
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : TryProperty(root, "data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array
                ? dataArr
                : default;
        if (array.ValueKind != JsonValueKind.Array) return Array.Empty<UbagBrowserContext>();

        var rows = new List<UbagBrowserContext>();
        foreach (var item in array.EnumerateArray())
        {
            var targetId = ReadString(item, "target_id")
                           ?? ReadString(item, "targetId")
                           ?? ReadString(item, "target");
            if (string.IsNullOrWhiteSpace(targetId)) continue;

            var loginState = ReadString(item, "login_state")
                             ?? ReadString(item, "loginState")
                             ?? ReadString(item, "state")
                             ?? "unknown";

            rows.Add(new UbagBrowserContext(targetId, loginState));
        }
        return rows;
    }

    public async Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct)
    {
        var body = new
        {
            api_version = ApiVersion(),
            client = ClientEnvelope(request.ClientRequestId),
            job = new
            {
                target = request.Target,
                command_type = request.CommandType,
                input = new { prompt = request.Prompt },
                options = new { return_mode = request.ReturnMode },
            },
        };
        using var doc = await SendAsync(HttpMethod.Post, "/v1/jobs", body, idempotencyKey, ct);
        return ParseJob(doc.RootElement, request.Target);
    }

    public async Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/v1/jobs/{Uri.EscapeDataString(jobId)}", null, null, ct);
        return ParseJob(doc.RootElement, "");
    }

    public async Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct)
    {
        // NOTE: the gateway's POST /v1/workflows schema is {api_version, name, steps[]}
        // and its decoder rejects unknown top-level fields. A `client` envelope (accepted
        // by /v1/jobs) is NOT valid here and yields UBAG-VALIDATION-JSON-001, so it is omitted.
        var body = new
        {
            api_version = ApiVersion(),
            name = request.Name,
            steps = request.Steps.Select(s => new
            {
                id = s.Id,
                target = s.Target,
                command = "submit",
                input = new { prompt = s.Prompt },
            }).ToArray(),
        };
        using var doc = await SendAsync(HttpMethod.Post, "/v1/workflows", body, idempotencyKey, ct);
        var root = doc.RootElement;
        return new UbagWorkflow(
            Id: ReadString(root, "id") ?? ReadString(root, "workflow_id") ?? "",
            Status: ReadString(root, "status") ?? "created",
            RawJson: root.GetRawText());
    }

    public async Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct)
    {
        var body = new { api_version = ApiVersion() };
        UbagWorkflowRun run;
        List<UbagStepRef> steps;
        using (var doc = await SendAsync(HttpMethod.Post, $"/v1/workflows/{Uri.EscapeDataString(workflowId)}/runs", body, idempotencyKey, ct))
        {
            run = ParseRun(doc.RootElement, workflowId);
            steps = ExtractSteps(doc.RootElement);
        }
        return await EnrichRunOutputAsync(run, steps, ct);
    }

    public async Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct)
    {
        UbagWorkflowRun run;
        List<UbagStepRef> steps;
        using (var doc = await SendAsync(HttpMethod.Get, $"/v1/workflows/runs/{Uri.EscapeDataString(runId)}", null, null, ct))
        {
            run = ParseRun(doc.RootElement, "");
            steps = ExtractSteps(doc.RootElement);
        }
        return await EnrichRunOutputAsync(run, steps, ct);
    }

    // The gateway marks a workflow run terminal as soon as its steps are DISPATCHED;
    // the live browser jobs finish later, and the run response carries only per-step
    // job ids (no answer text). So derive true completion + output from the step JOBS:
    // keep the run non-terminal until every step job is itself terminal, and aggregate
    // each step's text (result.output.text). The frontend auto-polls until terminal.
    private async Task<UbagWorkflowRun> EnrichRunOutputAsync(UbagWorkflowRun run, IReadOnlyList<UbagStepRef> steps, CancellationToken ct)
    {
        if (steps.Count == 0)
            return run;
        var parts = new List<string>();
        string? manual = null;
        string? error = null;
        var allDone = true;
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.JobId)) { allDone = false; continue; }
            UbagJob job;
            try { job = await GetJobAsync(step.JobId, ct); }
            catch (ProviderTransportException) { allDone = false; continue; }
            var stepDone = job.Terminal || !string.IsNullOrWhiteSpace(job.ManualAction);
            if (!stepDone) allDone = false;
            if (manual is null && !string.IsNullOrWhiteSpace(job.ManualAction)) manual = job.ManualAction;
            if (error is null && !string.IsNullOrWhiteSpace(job.Error)) error = $"{step.StepId}: {job.Error}";
            if (!string.IsNullOrWhiteSpace(job.Output))
            {
                var label = string.IsNullOrWhiteSpace(step.StepId) ? job.Target : step.StepId;
                parts.Add($"### {label}\n{job.Output}");
            }
        }
        return run with
        {
            Output = parts.Count > 0 ? string.Join("\n\n", parts) : run.Output,
            Terminal = run.Terminal && allDone,
            ManualAction = run.ManualAction ?? manual,
            Error = run.Error ?? error,
        };
    }

    private static List<UbagStepRef> ExtractSteps(JsonElement root)
    {
        var list = new List<UbagStepRef>();
        if (TryProperty(root, "steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in steps.EnumerateArray())
            {
                list.Add(new UbagStepRef(
                    ReadString(s, "step_id") ?? ReadString(s, "id") ?? "",
                    ReadString(s, "state") ?? ReadString(s, "status") ?? "",
                    ReadString(s, "job_id") ?? ReadString(s, "jobId") ?? ""));
            }
        }
        return list;
    }

    private readonly record struct UbagStepRef(string StepId, string State, string JobId);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        req.Headers.TryAddWithoutValidation("Ubag-Api-Version", ApiVersion());
        ApplyAuth(req);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new ProviderTransportException(
                $"ubag: HTTP {(int)res.StatusCode}",
                statusCode: (int)res.StatusCode,
                responseBody: Truncate(text));
        }

        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "UBAG returned non-JSON response for {Path}", path);
            throw new ProviderTransportException("ubag: malformed JSON response", inner: ex);
        }
    }

    private static object ClientEnvelope(string? clientRequestId) => new
    {
        app_id = "radiopad",
        app_version = "0.1.0",
        sdk = new { name = "radiopad-ubag-client", version = "1.0.0" },
    };

    private static string ApiVersion()
        => Environment.GetEnvironmentVariable("RADIOPAD_UBAG_API_VERSION") ?? DefaultApiVersion;

    private static void ApplyAuth(HttpRequestMessage req)
    {
        var secret = ResolveSecret(
            Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SECRET_REF"),
            Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SECRET"));
        if (string.IsNullOrWhiteSpace(secret)) return;

        var header = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_HEADER");
        var scheme = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SCHEME") ?? "Bearer";
        if (string.IsNullOrWhiteSpace(header) || string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.Authorization = BuildAuthorization(scheme, secret);
            return;
        }

        req.Headers.TryAddWithoutValidation(header.Trim(), secret);
    }

    private static AuthenticationHeaderValue BuildAuthorization(string scheme, string secret)
    {
        if (string.Equals(scheme, "Basic", StringComparison.OrdinalIgnoreCase) && secret.Contains(':', StringComparison.Ordinal))
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
            return new AuthenticationHeaderValue("Basic", encoded);
        }
        if (string.Equals(scheme, "Raw", StringComparison.OrdinalIgnoreCase))
        {
            var parts = secret.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2
                ? new AuthenticationHeaderValue(parts[0], parts[1])
                : new AuthenticationHeaderValue("Bearer", secret);
        }
        return new AuthenticationHeaderValue(scheme.Trim(), secret);
    }

    private static string? ResolveSecret(string? secretRef, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(secretRef)) return fallback;
        return secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable(secretRef[4..])
            : fallback;
    }

    private static UbagJob ParseJob(JsonElement root, string fallbackTarget)
    {
        var status = ReadString(root, "status") ?? ReadString(root, "state") ?? "unknown";
        return new UbagJob(
            Id: ReadString(root, "id") ?? ReadString(root, "job_id") ?? "",
            Target: ReadString(root, "target") ?? fallbackTarget,
            Status: status,
            Terminal: IsTerminal(status),
            Output: ExtractOutput(root),
            Error: ReadString(root, "error") ?? ReadString(root, "message"),
            ManualAction: ReadString(root, "manual_action") ?? ReadString(root, "manualAction"),
            LatencyMs: ReadInt(root, "latency_ms") ?? ReadInt(root, "latencyMs"),
            RawJson: root.GetRawText());
    }

    private static UbagWorkflowRun ParseRun(JsonElement root, string fallbackWorkflowId)
    {
        var status = ReadString(root, "status") ?? ReadString(root, "state") ?? "unknown";
        return new UbagWorkflowRun(
            Id: ReadString(root, "id") ?? ReadString(root, "run_id") ?? "",
            WorkflowId: ReadString(root, "workflow_id") ?? ReadString(root, "workflowId") ?? fallbackWorkflowId,
            Status: status,
            Terminal: IsTerminal(status),
            Output: ExtractOutput(root),
            Error: ReadString(root, "error") ?? ReadString(root, "message"),
            ManualAction: ReadString(root, "manual_action") ?? ReadString(root, "manualAction"),
            RawJson: root.GetRawText());
    }

    private static string? ExtractOutput(JsonElement root)
    {
        // The gateway returns completed-job text at result.output.text (two levels deep),
        // so descend recursively rather than only one level.
        foreach (var key in new[] { "output", "text", "final", "result" })
        {
            if (!TryProperty(root, key, out var prop)) continue;
            var found = TextFrom(prop, depth: 0);
            if (!string.IsNullOrWhiteSpace(found)) return found;
        }
        return null;
    }

    private static string? TextFrom(JsonElement el, int depth)
    {
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind != JsonValueKind.Object || depth > 4) return null;
        foreach (var nested in new[] { "text", "plain_text", "markdown", "html", "final", "content", "value" })
        {
            var v = ReadString(el, nested);
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        foreach (var container in new[] { "output", "result", "data", "message" })
        {
            if (TryProperty(el, container, out var child))
            {
                var v = TextFrom(child, depth + 1);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        return null;
    }

    // Gateway terminal statuses (jobs + workflow runs). "failed_retryable" is intentionally
    // NOT terminal so polling continues until it resolves or the caller's timeout fires.
    private static readonly string[] TerminalStatuses =
    {
        "completed", "complete", "completed_with_warnings", "succeeded",
        "failed", "failed_terminal", "dead_letter", "cancelled", "canceled", "timed_out",
    };

    private static bool IsTerminal(string status)
        => TerminalStatuses.Any(s => status.Equals(s, StringComparison.OrdinalIgnoreCase));

    private static bool IsOkStatus(string status)
        => status.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ready", StringComparison.OrdinalIgnoreCase)
            || status.Equals("healthy", StringComparison.OrdinalIgnoreCase)
            || status.Equals("connected", StringComparison.OrdinalIgnoreCase);

    private static bool TryProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        if (root.TryGetProperty(name, out value)) return true;
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement root, string name)
        => TryProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
        => TryProperty(root, name, out var value) && value.TryGetInt32(out var n) ? n : null;

    private static bool? ReadBool(JsonElement root, string name)
        => TryProperty(root, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string Truncate(string s) => s.Length > 2048 ? s[..2048] : s;
}
