using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Services;

/// <summary>
/// Routes AI completion requests across registered provider adapters while
/// enforcing PHI policy (PROV-001..010, §14.3). The gateway is the only
/// component allowed to call provider APIs in production.
/// </summary>
public class AiGateway : IAiGateway
{
    private readonly IReadOnlyDictionary<string, IAiProviderAdapter> _adapters;
    private readonly IAuditLog _audit;
    private readonly IAiUsageStore? _usage;
    private readonly PlanQuotaService? _quota;
    private readonly IPlanQuotaStore? _quotaStore;
    private readonly ILogger<AiGateway> _log;

    public AiGateway(
        IEnumerable<IAiProviderAdapter> adapters,
        IAuditLog audit,
        ILogger<AiGateway> log,
        IAiUsageStore? usage = null,
        PlanQuotaService? quota = null,
        IPlanQuotaStore? quotaStore = null)
    {
        _adapters = adapters.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        _audit = audit;
        _log = log;
        _usage = usage;
        _quota = quota;
        _quotaStore = quotaStore;
    }

    public async Task<AiResult> RouteAsync(
        Tenant tenant,
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            EnforcePhiPolicy(tenant, request);
        }
        catch (ProviderPolicyException pex)
        {
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = request.Provider.Name,
                    adapter = request.Provider.Adapter,
                    compliance = request.Provider.Compliance.ToString(),
                    containsPhi = request.ContainsPhi,
                    reason = pex.Message,
                }),
            }, cancellationToken);
            await TryRecordUsageAsync(tenant, request, "blocked", inputHash: Sha256(request.UserPrompt), outputHash: "", latencyMs: 0, inputTokens: 0, outputTokens: 0, cancellationToken);
            throw;
        }

        // PRD BILL-001..006 — plan quota gate. Only enforced when both the
        // service and store are wired (DI default in Program.cs); tests that
        // construct the gateway directly without billing helpers stay
        // permissive so PHI-policy fixtures keep their original behaviour.
        if (_quota is not null && _quotaStore is not null)
        {
            // BUG-3 fix: a tenant without a TenantSettings row is treated as an
            // implicit Trial-defaults tenant so the quota gate never silently
            // bypasses. The in-memory row is NOT persisted here.
            var settings = await _quotaStore.LoadSettingsAsync(tenant.Id, cancellationToken)
                ?? new TenantSettings { TenantId = tenant.Id };
            {
                var quota = await _quota.CheckAsync(tenant, settings, cancellationToken);
                if (!quota.AllowedToProceed)
                {
                    await _audit.AppendAsync(new AuditEvent
                    {
                        TenantId = tenant.Id,
                        UserId = Guid.Empty,
                        Action = AuditAction.PolicyViolation,
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            kind = "quota_exceeded",
                            reason = quota.Reason,
                            plan = quota.Plan.ToString(),
                            aiCallsUsed = quota.AiCallsUsed,
                            aiCallsLimit = quota.AiCallsLimit,
                            inputTokensUsed = quota.InputTokensUsed,
                            inputTokensLimit = quota.InputTokensLimit,
                            outputTokensUsed = quota.OutputTokensUsed,
                            outputTokensLimit = quota.OutputTokensLimit,
                        }),
                    }, cancellationToken);
                    throw new QuotaExceededException(quota.Reason ?? "quota_exceeded", new
                    {
                        plan = quota.Plan.ToString(),
                        aiCallsUsed = quota.AiCallsUsed,
                        aiCallsLimit = quota.AiCallsLimit,
                        inputTokensUsed = quota.InputTokensUsed,
                        inputTokensLimit = quota.InputTokensLimit,
                        outputTokensUsed = quota.OutputTokensUsed,
                        outputTokensLimit = quota.OutputTokensLimit,
                        resetAt = quota.ResetAt,
                    });
                }
            }
        }

        if (!_adapters.TryGetValue(request.Provider.Adapter, out var adapter))
            throw new InvalidOperationException($"AI adapter '{request.Provider.Adapter}' is not registered.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await adapter.CompleteAsync(request, cancellationToken);
            sw.Stop();
            var inputHash = Sha256(request.UserPrompt);
            var outputHash = Sha256(result.Text);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.AiResponse,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = request.Provider.Name,
                    adapter = request.Provider.Adapter,
                    model = request.Provider.Model,
                    promptVersion = request.PromptVersion,
                    // Iter-0b (RB-009 / AI-012) — rulebook provenance on every AI event.
                    rulebookId = request.RulebookId,
                    rulebookVersion = request.RulebookVersion,
                    temperature = request.Temperature,
                    inputHash,
                    outputHash,
                    latencyMs = sw.ElapsedMilliseconds,
                    inputTokens = result.InputTokens,
                    outputTokens = result.OutputTokens,
                    containsPhi = request.ContainsPhi,
                }),
            }, cancellationToken);
            await TryRecordUsageAsync(tenant, request, "ok", inputHash, outputHash, (int)sw.ElapsedMilliseconds, result.InputTokens, result.OutputTokens, cancellationToken);
            return result;
        }
        catch (ProviderPolicyException ex)
        {
            sw.Stop();
            _log.LogWarning(ex, "AI provider {Adapter} blocked request by provider policy", request.Provider.Adapter);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = request.Provider.Name,
                    adapter = request.Provider.Adapter,
                    compliance = request.Provider.Compliance.ToString(),
                    containsPhi = request.ContainsPhi,
                    reason = ex.Message,
                }),
            }, cancellationToken);
            await TryRecordUsageAsync(tenant, request, "blocked", inputHash: Sha256(request.UserPrompt), outputHash: "", latencyMs: (int)sw.ElapsedMilliseconds, inputTokens: 0, outputTokens: 0, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AI provider {Adapter} failed", request.Provider.Adapter);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = request.Provider.Name,
                    error = ex.GetType().Name,
                    message = ex.Message,
                }),
            }, cancellationToken);
            await TryRecordUsageAsync(tenant, request, "error", inputHash: Sha256(request.UserPrompt), outputHash: "", latencyMs: (int)sw.ElapsedMilliseconds, inputTokens: 0, outputTokens: 0, cancellationToken);
            throw;
        }
    }

    private async Task TryRecordUsageAsync(
        Tenant tenant, AiCompletionRequest request, string status,
        string inputHash, string outputHash, int latencyMs, int inputTokens, int outputTokens,
        CancellationToken ct)
    {
        if (_usage is null) return;
        try
        {
            await _usage.RecordAsync(new AiRequest
            {
                TenantId = tenant.Id,
                Provider = request.Provider.Name,
                Model = request.Provider.Model,
                Mode = "ai",
                ContainsPhi = request.ContainsPhi,
                PromptVersion = request.PromptVersion,
                // Iter-0b (RB-009) — rulebook provenance on the usage ledger row.
                RulebookId = request.RulebookId,
                RulebookVersion = request.RulebookVersion ?? "",
                InputHash = inputHash,
                OutputHash = outputHash,
                LatencyMs = latencyMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Status = status,
            }, ct);
        }
        catch (Exception ex)
        {
            // Usage ledger must never break the AI path; surface the loss in logs only.
            _log.LogWarning(ex, "Failed to write AiRequest usage row for tenant {Tenant}", tenant.Slug);
        }
    }

    /// <summary>
    /// PROV-001..010 / §14.3 — PHI may only reach a PhiApproved or LocalOnly provider.
    ///
    /// <para><b>RESTORED 2026-07-20.</b> This gate was deleted in commit bf1fbf4 with an inline
    /// comment attributing the removal to an operator decision. No such decision is recorded
    /// anywhere: CLAUDE.md still lists this as a non-negotiable safety boundary, the two tests
    /// that assert it (<c>RewriteModeTests.Rewrite_Phi_Policy_Blocks_NonCompliant_Provider</c>
    /// and <c>AiPolicyHttpTests.Sandbox_Provider_Rejects_Phi_Bearing_Request</c>) were left
    /// failing rather than updated, and no doc or regulatory note changed. A real decision to
    /// route PHI to arbitrary third-party providers would have moved all of those together.</para>
    ///
    /// <para>Removing it means PHI leaving the tenant's approved boundary — a reportable breach
    /// under UKCA/MHRA/CE/FDA obligations, not a config preference. If the removal IS wanted,
    /// it must be done deliberately: update CLAUDE.md, rewrite these tests to assert the new
    /// behaviour, and record the regulatory rationale here.</para>
    /// </summary>
    private static void EnforcePhiPolicy(Tenant tenant, AiCompletionRequest req)
    {
        var p = req.Provider;
        if (!p.Enabled)
            throw new ProviderPolicyException($"Provider '{p.Name}' is disabled.");
        if (p.Compliance == ProviderComplianceClass.Blocked)
            throw new ProviderPolicyException($"Provider '{p.Name}' is blocked by tenant policy.");

        if (req.ContainsPhi)
        {
            var phiOk = p.Compliance is ProviderComplianceClass.PhiApproved or ProviderComplianceClass.LocalOnly;
            if (!phiOk)
                throw new ProviderPolicyException(
                    $"Cannot route PHI to '{p.Name}' (compliance={p.Compliance}). " +
                    $"Tenant '{tenant.Slug}' requires a PHI-approved or local provider for PHI workflows.");
        }
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public class ProviderPolicyException : Exception
{
    public ProviderPolicyException(string message) : base(message) { }
}

/// <summary>
/// Raised by an <see cref="IAiProviderAdapter"/> when the upstream HTTP
/// transport fails (non-2xx status, network error, malformed response, or
/// timeout). The gateway maps this to <c>kind=provider_transport</c> in
/// RFC-7807 responses so operators can distinguish transport failures from
/// PHI-policy blocks.
/// </summary>
public class ProviderTransportException : Exception
{
    public int? StatusCode { get; }
    public string? ResponseBody { get; }

    public ProviderTransportException(string message, int? statusCode = null, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Raised when an AI run references a non-Approved rulebook in a tenant that
/// does not allow sandbox rulebooks (PRD RB-010).
/// </summary>
public class RulebookGovernanceException : Exception
{
    public RulebookGovernanceException(string message) : base(message) { }
}
