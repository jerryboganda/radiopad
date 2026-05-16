using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Cli;

namespace RadioPad.Api.Services;

public record CopilotSettingsDto(
    bool Enabled,
    bool EmergencyDisabled,
    string DefaultMode,
    string[] AllowedModes,
    string GitHubEnterpriseSlug,
    string GitHubOrganization,
    string GitHubHost,
    bool SdkRuntimeEnabled,
    bool CliRuntimeEnabled,
    bool AllowByoAccounts,
    bool AllowEnvironmentTokenAuth,
    bool RequireOsKeychainForCli,
    bool PromptLoggingEnabled,
    bool ContextLoggingEnabled,
    string RetentionPolicy,
    string PolicyJson,
    string GitHubAppId,
    string GitHubAppInstallationId,
    string OAuthClientId,
    bool GitHubAppPrivateKeyConfigured,
    bool OAuthClientSecretConfigured,
    string? GitHubAppPrivateKeySecretRef = null,
    string? OAuthClientSecretRef = null);

public record CopilotFeatureDto(string FeatureKey, bool Enabled, string RequiredRole, string PolicyJson);
public record CopilotStatusDto(
    bool Enabled,
    bool EmergencyDisabled,
    string DefaultMode,
    string RuntimeStatus,
    string Kind,
    string Message,
    string[] AllowedModes,
    bool PhiBlocked,
    bool PromptLoggingEnabled,
    bool ContextLoggingEnabled,
    string GitHubHost,
    string GitHubOrganization,
    string[] UnsupportedFeatures);

public record CopilotAccountDto(
    string Mode,
    string GitHubLogin,
    string TokenStatus,
    string SsoStatus,
    string SeatStatus,
    string DenialReason,
    DateTimeOffset? LastAuthenticatedAt,
    DateTimeOffset? RevokedAt,
    bool EntitlementAllowed,
    string EntitlementSource);

public record CopilotEntitlementDto(
    bool Allowed,
    string Mode,
    string Source,
    string GitHubLogin,
    string SsoStatus,
    string SeatStatus,
    string DenialReason,
    DateTimeOffset CheckedAt,
    DateTimeOffset? ExpiresAt);

public record CopilotAuthStartRequest(string? Mode, string? RedirectUri);
public record CopilotAuthStartDto(
    string Mode,
    string Kind,
    string Message,
    string? AuthorizationUrl,
    string? DesktopCommand,
    string State);

public record CopilotLocalCliAccountRequest(
    string? GitHubLogin,
    long? GitHubUserId,
    string? Host,
    string? SsoStatus,
    string? SeatStatus);

public record CopilotContextItemDto(string Kind, string Label, string Text);
public record CopilotContextRemovalDto(string Label, string Reason);
public record CopilotContextPreviewRequest(string? Message, string? ContextKind, CopilotContextItemDto[]? Items);
public record CopilotContextPreviewDto(
    string MessageHash,
    bool ContainsPhi,
    CopilotContextItemDto[] Included,
    CopilotContextRemovalDto[] Removed,
    string ContextHash);

public record CopilotChatRequest(string? Message, string? SessionId, string? Mode, string? ContextKind);
public record CopilotSessionRequest(string? Message, string? Mode, string? ContextKind, CopilotContextItemDto[]? Context);
public record CopilotSessionDto(
    Guid SessionId,
    string Status,
    string Mode,
    string Runtime,
    string Message,
    string? Output,
    string? ErrorKind,
    CopilotContextPreviewDto Context,
    int LatencyMs);
public record CopilotErrorDto(string Kind, string Message, string RuntimeStatus, string RequestId);
public record CopilotSessionResult(CopilotSessionDto? Session, CopilotErrorDto? Error, int StatusCode);

public record CopilotQuotaPolicyDto(
    Guid? Id,
    string ScopeType,
    string ScopeKey,
    string Feature,
    int WindowSeconds,
    int MaxRequests,
    int MaxConcurrent,
    bool Enabled);

public record CopilotQuotaStatusDto(
    bool Allowed,
    string Kind,
    string Message,
    string ScopeType,
    string ScopeKey,
    string Feature,
    int Used,
    int Limit,
    int Running,
    int ConcurrentLimit);

public record CopilotUsageBucketDto(string Status, int Count);
public record CopilotUsageSummaryDto(int Total, int Completed, int Blocked, int Failed, int Cancelled, int Running, CopilotUsageBucketDto[] ByStatus);

public class CopilotService
{
    private const int MaxMessageChars = 8000;
    private const int MaxContextChars = 12000;
    private const string DefaultCopilotBinary = "gh";
    private readonly RadioPadDbContext _db;
    private readonly IProcessLauncher _processLauncher;

    public CopilotService(RadioPadDbContext db, IProcessLauncher? processLauncher = null)
    {
        _db = db;
        _processLauncher = processLauncher ?? new DefaultProcessLauncher();
    }

    public async Task<CopilotIntegrationSettings> GetOrCreateSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var row = await _db.CopilotIntegrationSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (row is not null) return row;
        row = new CopilotIntegrationSettings { TenantId = tenantId };
        _db.CopilotIntegrationSettings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public CopilotSettingsDto ToDto(CopilotIntegrationSettings s) => new(
        s.Enabled,
        s.EmergencyDisabled,
        s.DefaultMode.ToString(),
        SplitModes(s.AllowedModes),
        s.GitHubEnterpriseSlug,
        s.GitHubOrganization,
        s.GitHubHost,
        s.SdkRuntimeEnabled,
        s.CliRuntimeEnabled,
        s.AllowByoAccounts,
        s.AllowEnvironmentTokenAuth,
        s.RequireOsKeychainForCli,
        s.PromptLoggingEnabled,
        s.ContextLoggingEnabled,
        s.RetentionPolicy,
        s.PolicyJson,
        s.GitHubAppId,
        s.GitHubAppInstallationId,
        s.OAuthClientId,
        !string.IsNullOrWhiteSpace(s.GitHubAppPrivateKeySecretRef),
        !string.IsNullOrWhiteSpace(s.OAuthClientSecretRef),
        null,
        null);

    public (bool ok, string? error, string? kind) Apply(CopilotIntegrationSettings s, CopilotSettingsDto dto)
    {
        if (!TryParseMode(dto.DefaultMode, out var mode))
            return (false, "defaultMode must be one of Disabled, EnterpriseManaged, BringYourOwnAccount, LocalCli, Byok.", "validation");

        var allowed = (dto.AllowedModes?.Length > 0 ? dto.AllowedModes : new[] { "Disabled" })
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (allowed.Length == 0 || allowed.Any(x => !TryParseMode(x!, out _)))
            return (false, "allowedModes contains an unsupported Copilot mode.", "validation");
        if (!allowed.Any(x => string.Equals(x, mode.ToString(), StringComparison.OrdinalIgnoreCase)))
            return (false, "defaultMode must be included in allowedModes.", "validation");
        if (dto.PromptLoggingEnabled || dto.ContextLoggingEnabled)
            return (false, "Copilot prompt/context logging is not available in this production slice.", "prompt_logging_not_supported");
        if (!string.Equals(dto.RetentionPolicy, "metadata_only", StringComparison.OrdinalIgnoreCase))
            return (false, "Only metadata_only retention is supported for Copilot until a reviewed redaction pipeline is enabled.", "retention_policy_not_supported");
        if (!LooksLikeJsonObject(dto.PolicyJson))
            return (false, "policyJson must be a JSON object.", "validation");
        if (!AllowedSecretRef(dto.GitHubAppPrivateKeySecretRef) || !AllowedSecretRef(dto.OAuthClientSecretRef))
            return (false, "Secret references must be empty or start with env:, vault:, kms:, aws:, azkv:, or gcp:.", "secret_ref_required");

        s.Enabled = dto.Enabled;
        s.EmergencyDisabled = dto.EmergencyDisabled || !dto.Enabled;
        s.DefaultMode = mode;
        s.AllowedModes = string.Join(",", allowed.Select(NormalizeMode));
        s.GitHubEnterpriseSlug = Clean(dto.GitHubEnterpriseSlug, 128);
        s.GitHubOrganization = Clean(dto.GitHubOrganization, 128);
        s.GitHubHost = string.IsNullOrWhiteSpace(dto.GitHubHost) ? "github.com" : Clean(dto.GitHubHost, 200);
        s.SdkRuntimeEnabled = dto.SdkRuntimeEnabled;
        s.CliRuntimeEnabled = dto.CliRuntimeEnabled;
        s.AllowByoAccounts = dto.AllowByoAccounts;
        s.AllowEnvironmentTokenAuth = dto.AllowEnvironmentTokenAuth;
        s.RequireOsKeychainForCli = dto.RequireOsKeychainForCli;
        s.PromptLoggingEnabled = false;
        s.ContextLoggingEnabled = false;
        s.RetentionPolicy = "metadata_only";
        s.PolicyJson = dto.PolicyJson.Trim();
        s.GitHubAppId = Clean(dto.GitHubAppId, 80);
        s.GitHubAppInstallationId = Clean(dto.GitHubAppInstallationId, 80);
        s.OAuthClientId = Clean(dto.OAuthClientId, 120);
        var secretsChanged = false;
        if (!string.IsNullOrWhiteSpace(dto.GitHubAppPrivateKeySecretRef))
        {
            s.GitHubAppPrivateKeySecretRef = dto.GitHubAppPrivateKeySecretRef.Trim();
            secretsChanged = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.OAuthClientSecretRef))
        {
            s.OAuthClientSecretRef = dto.OAuthClientSecretRef.Trim();
            secretsChanged = true;
        }
        if (secretsChanged) s.SecretsUpdatedAt = DateTimeOffset.UtcNow;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        return (true, null, null);
    }

    public CopilotStatusDto Status(CopilotIntegrationSettings s)
    {
        var runtime = RuntimeStatus(s);
        var kind = runtime switch
        {
            CopilotRuntimeStatus.Disabled => "copilot_disabled",
            CopilotRuntimeStatus.NotConfigured => "runtime_not_configured",
            CopilotRuntimeStatus.RuntimeUnavailable => "runtime_unavailable",
            _ => "ready",
        };
        var message = runtime == CopilotRuntimeStatus.Ready
            ? "Copilot policy gates are ready for the selected official local CLI runtime. Prompts are still filtered, quota-gated, and metadata-only."
            : "Copilot is fail-closed. Configure LocalCli with the official GitHub CLI/Copilot extension, or keep SDK modes unavailable until a reviewed server SDK transport is installed.";
        return new CopilotStatusDto(
            s.Enabled,
            s.EmergencyDisabled,
            s.DefaultMode.ToString(),
            runtime.ToString(),
            kind,
            message,
            SplitModes(s.AllowedModes),
            PhiBlocked: true,
            s.PromptLoggingEnabled,
            s.ContextLoggingEnabled,
            s.GitHubHost,
            s.GitHubOrganization,
            new[]
            {
                "IDE token scraping",
                "undocumented Copilot endpoints",
                "shared admin token impersonation",
                "frontend or IPC token exposure",
                "PHI prompt routing",
            });
    }

    public async Task<CopilotAccountDto> AccountAsync(Guid tenantId, Guid userId, CopilotIntegrationSettings settings, CancellationToken ct)
    {
        var account = await _db.CopilotUserAccounts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        var entitlement = await RefreshEntitlementAsync(tenantId, userId, settings, account, ct);
        if (account is null)
        {
            return new CopilotAccountDto(
                settings.DefaultMode.ToString(),
                "",
                "none",
                "unknown",
                "unknown",
                entitlement.DenialReason,
                null,
                null,
                entitlement.Allowed,
                entitlement.Source);
        }
        return new CopilotAccountDto(
            account.Mode.ToString(),
            account.GitHubLogin,
            string.IsNullOrWhiteSpace(account.TokenSecretRef) ? account.TokenStatus : "configured",
            account.SsoStatus,
            account.SeatStatus,
            entitlement.DenialReason,
            account.LastAuthenticatedAt,
            account.RevokedAt,
            entitlement.Allowed,
            entitlement.Source);
    }

    public async Task<CopilotAuthStartDto> BeginAuthAsync(Guid tenantId, Guid userId, CopilotIntegrationSettings settings, CopilotAuthStartRequest request, CancellationToken ct)
    {
        var mode = ParseRequestedMode(request.Mode, settings);
        var state = Hash($"{tenantId:N}:{userId:N}:{mode}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        if (!ModeAllowed(settings, mode))
        {
            return new CopilotAuthStartDto(mode.ToString(), "policy_blocked", "This Copilot mode is not allowed by tenant policy.", null, null, state);
        }
        if (mode == CopilotMode.LocalCli)
        {
            return new CopilotAuthStartDto(
                mode.ToString(),
                "desktop_cli",
                "Use the token-free Tauri CLI bridge. The official GitHub CLI stores credentials in its own OS keychain and RadioPad receives only status metadata.",
                null,
                "copilot_cli_login_begin",
                state);
        }
        if (mode == CopilotMode.BringYourOwnAccount && !settings.AllowByoAccounts)
        {
            return new CopilotAuthStartDto(mode.ToString(), "policy_blocked", "Bring-your-own GitHub accounts are disabled for this tenant.", null, null, state);
        }
        if (string.IsNullOrWhiteSpace(settings.OAuthClientId))
        {
            return new CopilotAuthStartDto(mode.ToString(), "oauth_not_configured", "OAuth client ID is not configured. Use LocalCli or ask an admin to configure GitHub OAuth.", null, null, state);
        }

        var redirect = string.IsNullOrWhiteSpace(request.RedirectUri) ? "" : request.RedirectUri.Trim();
        var url = new StringBuilder("https://github.com/login/oauth/authorize");
        url.Append("?client_id=").Append(Uri.EscapeDataString(settings.OAuthClientId));
        url.Append("&scope=").Append(Uri.EscapeDataString("read:user read:org"));
        url.Append("&state=").Append(Uri.EscapeDataString(state));
        if (redirect.Length > 0) url.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirect));
        await Task.CompletedTask;
        return new CopilotAuthStartDto(mode.ToString(), "oauth_authorize", "Open the official GitHub OAuth authorization URL. Token exchange must land in a backend/vault secret reference, never the WebView.", url.ToString(), null, state);
    }

    public async Task<CopilotAccountDto> LinkLocalCliAccountAsync(Guid tenantId, Guid userId, CopilotIntegrationSettings settings, CopilotLocalCliAccountRequest request, CancellationToken ct)
    {
        if (!ModeAllowed(settings, CopilotMode.LocalCli))
            throw new InvalidOperationException("local_cli_mode_not_allowed");
        var account = await _db.CopilotUserAccounts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        if (account is null)
        {
            account = new CopilotUserAccount { TenantId = tenantId, UserId = userId };
            _db.CopilotUserAccounts.Add(account);
        }
        account.Mode = CopilotMode.LocalCli;
        account.GitHubLogin = Clean(request.GitHubLogin, 120);
        account.GitHubUserId = request.GitHubUserId;
        account.TokenStatus = string.IsNullOrWhiteSpace(account.GitHubLogin) ? "missing" : "cli_keychain";
        account.TokenSecretRef = "";
        account.SsoStatus = Clean(request.SsoStatus, 80);
        if (string.IsNullOrWhiteSpace(account.SsoStatus)) account.SsoStatus = "local_cli";
        account.SeatStatus = Clean(request.SeatStatus, 80);
        if (string.IsNullOrWhiteSpace(account.SeatStatus)) account.SeatStatus = "cli_enforced";
        account.DenialReason = "";
        account.LastAuthenticatedAt = DateTimeOffset.UtcNow;
        account.RevokedAt = null;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await RefreshEntitlementAsync(tenantId, userId, settings, account, ct);
        await _db.SaveChangesAsync(ct);
        return await AccountAsync(tenantId, userId, settings, ct);
    }

    public async Task RevokeAccountAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var account = await _db.CopilotUserAccounts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        if (account is not null)
        {
            account.TokenSecretRef = "";
            account.TokenStatus = "revoked";
            account.DenialReason = "revoked";
            account.RevokedAt = DateTimeOffset.UtcNow;
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }
        var entitlements = await _db.CopilotEntitlements.Where(x => x.TenantId == tenantId && x.UserId == userId).ToArrayAsync(ct);
        foreach (var entitlement in entitlements)
        {
            entitlement.Allowed = false;
            entitlement.DenialReason = "revoked";
            entitlement.CheckedAt = DateTimeOffset.UtcNow;
            entitlement.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<CopilotEntitlementDto> RefreshEntitlementAsync(Guid tenantId, Guid userId, CopilotIntegrationSettings settings, CopilotUserAccount? account, CancellationToken ct)
    {
        var mode = account?.Mode ?? settings.DefaultMode;
        var entitlement = await _db.CopilotEntitlements.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.Mode == mode, ct);
        if (entitlement is null)
        {
            entitlement = new CopilotEntitlement { TenantId = tenantId, UserId = userId, Mode = mode };
            _db.CopilotEntitlements.Add(entitlement);
        }

        var (allowed, reason, source) = EvaluateEntitlement(settings, account, mode);
        entitlement.Allowed = allowed;
        entitlement.Source = source;
        entitlement.GitHubLogin = account?.GitHubLogin ?? "";
        entitlement.SsoStatus = account?.SsoStatus ?? "unknown";
        entitlement.SeatStatus = account?.SeatStatus ?? "unknown";
        entitlement.DenialReason = reason;
        entitlement.CheckedAt = DateTimeOffset.UtcNow;
        entitlement.ExpiresAt = allowed ? DateTimeOffset.UtcNow.AddMinutes(15) : null;
        entitlement.UpdatedAt = DateTimeOffset.UtcNow;
        if (account is not null) account.DenialReason = reason;

        return ToEntitlementDto(entitlement);
    }

    public async Task<CopilotEntitlementDto> EntitlementAsync(Guid tenantId, Guid userId, CopilotIntegrationSettings settings, CancellationToken ct)
    {
        var account = await _db.CopilotUserAccounts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        return await RefreshEntitlementAsync(tenantId, userId, settings, account, ct);
    }

    public CopilotContextPreviewDto PreviewContext(CopilotContextPreviewRequest request)
    {
        var removals = new List<CopilotContextRemovalDto>();
        var included = new List<CopilotContextItemDto>();
        var contextKind = Clean(request.ContextKind, 80);
        var containsPhi = LooksLikePhi(request.Message) || IsClinicalContext(contextKind);

        foreach (var item in request.Items ?? Array.Empty<CopilotContextItemDto>())
        {
            var label = Clean(item.Label, 240);
            var text = item.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                removals.Add(new CopilotContextRemovalDto(label, "empty"));
                continue;
            }
            if (text.Length > MaxContextChars)
            {
                removals.Add(new CopilotContextRemovalDto(label, "too_large"));
                continue;
            }
            if (LooksBinary(text) || LooksLikeExcludedPath(label))
            {
                removals.Add(new CopilotContextRemovalDto(label, "excluded_file"));
                continue;
            }
            if (LooksLikeSecret(text))
            {
                removals.Add(new CopilotContextRemovalDto(label, "secret_redacted"));
                continue;
            }
            if (LooksLikePhi(text) || IsClinicalContext(item.Kind) || IsClinicalContext(label))
            {
                containsPhi = true;
                removals.Add(new CopilotContextRemovalDto(label, "phi_policy"));
                continue;
            }
            included.Add(new CopilotContextItemDto(Clean(item.Kind, 80), label, RedactSecrets(text)));
        }

        var canonical = JsonSerializer.Serialize(included.Select(x => new { x.Kind, x.Label, hash = Hash(x.Text) }));
        return new CopilotContextPreviewDto(
            Hash(request.Message),
            containsPhi,
            included.ToArray(),
            removals.ToArray(),
            Hash(canonical));
    }

    public async Task<CopilotSessionResult> StartSessionAsync(Guid tenantId, Guid userId, string requestId, CopilotIntegrationSettings settings, CopilotSessionRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var mode = ParseRequestedMode(request.Mode, settings);
        var preview = PreviewContext(new CopilotContextPreviewRequest(request.Message, request.ContextKind, request.Context));
        if (string.IsNullOrWhiteSpace(request.Message))
            return Error("validation", "Message is required.", Status(settings).RuntimeStatus, requestId, StatusCodes.Status400BadRequest);
        if (request.Message.Length > MaxMessageChars)
            return Error("validation", $"Message must be {MaxMessageChars} characters or less.", Status(settings).RuntimeStatus, requestId, StatusCodes.Status400BadRequest);
        if (!ModeAllowed(settings, mode))
            return await BlockAsync(tenantId, userId, requestId, request, settings, preview, "mode_not_allowed", "This Copilot mode is not allowed by tenant policy.", ct);
        if (preview.ContainsPhi)
            return await BlockAsync(tenantId, userId, requestId, request, settings, preview, "phi_policy", "Clinical/PHI context is blocked from GitHub Copilot.", ct);

        var account = await _db.CopilotUserAccounts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        var entitlement = await RefreshEntitlementAsync(tenantId, userId, settings, account, ct);
        if (!entitlement.Allowed)
            return await BlockAsync(tenantId, userId, requestId, request, settings, preview, entitlement.DenialReason, "Copilot entitlement gate blocked this request.", ct);

        var quota = await CheckQuotaAsync(tenantId, userId, request.ContextKind ?? "chat", ct);
        if (!quota.Allowed)
            return await BlockAsync(tenantId, userId, requestId, request, settings, preview, quota.Kind, quota.Message, ct);

        if (mode != CopilotMode.LocalCli || !settings.CliRuntimeEnabled)
            return await BlockAsync(tenantId, userId, requestId, request, settings, preview, "runtime_not_configured", "Only the official local CLI runtime is enabled in this implementation path.", ct);

        var session = new CopilotSession
        {
            TenantId = tenantId,
            UserId = userId,
            Mode = mode,
            Feature = Clean(request.ContextKind, 80),
            ContextKind = Clean(request.ContextKind, 80),
            Status = "running",
            Runtime = "gh-copilot-cli",
            ContextHash = preview.ContextHash,
            StartedAt = DateTimeOffset.UtcNow,
        };
        if (string.IsNullOrWhiteSpace(session.Feature)) session.Feature = "chat";
        if (string.IsNullOrWhiteSpace(session.ContextKind)) session.ContextKind = "manual";
        _db.CopilotSessions.Add(session);
        var message = new CopilotMessage
        {
            TenantId = tenantId,
            SessionId = session.Id,
            UserId = userId,
            Role = "user",
            Sequence = 1,
            Status = "running",
            InputHash = Hash(request.Message),
            Model = "gh-copilot",
        };
        _db.CopilotMessages.Add(message);
        _db.CopilotUsageEvents.Add(new CopilotUsageEvent
        {
            TenantId = tenantId,
            UserId = userId,
            RequestId = requestId,
            Feature = session.Feature,
            Mode = mode.ToString(),
            Status = "running",
            InputHash = message.InputHash,
        });
        await _db.SaveChangesAsync(ct);

        try
        {
            var prompt = BuildPrompt(request.Message!, preview.Included);
            var output = await RunLocalCliAsync(prompt, ct);
            sw.Stop();
            session.Status = "completed";
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            message.Status = "completed";
            message.OutputHash = Hash(output);
            message.LatencyMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            message.UpdatedAt = DateTimeOffset.UtcNow;
            _db.CopilotUsageEvents.Add(new CopilotUsageEvent
            {
                TenantId = tenantId,
                UserId = userId,
                RequestId = requestId,
                Feature = session.Feature,
                Mode = mode.ToString(),
                Status = "completed",
                LatencyMs = message.LatencyMs,
                InputHash = message.InputHash,
                OutputHash = message.OutputHash,
            });
            await _db.SaveChangesAsync(ct);
            return new CopilotSessionResult(new CopilotSessionDto(session.Id, session.Status, mode.ToString(), session.Runtime, "Copilot CLI completed.", output, null, preview, message.LatencyMs), null, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            session.Status = "cancelled";
            session.CancelledAt = DateTimeOffset.UtcNow;
            session.LastErrorKind = "cancelled";
            message.Status = "cancelled";
            message.LatencyMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            _db.CopilotUsageEvents.Add(new CopilotUsageEvent
            {
                TenantId = tenantId,
                UserId = userId,
                RequestId = requestId,
                Feature = session.Feature,
                Mode = mode.ToString(),
                Status = "cancelled",
                BlockKind = "cancelled",
                LatencyMs = message.LatencyMs,
                InputHash = message.InputHash,
            });
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is ProcessLaunchNotFoundException or ProcessLaunchTimeoutException or InvalidOperationException)
        {
            sw.Stop();
            var kind = ex is ProcessLaunchNotFoundException ? "cli_not_found" : ex is ProcessLaunchTimeoutException ? "cli_timeout" : "cli_failed";
            session.Status = "failed";
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.LastErrorKind = kind;
            message.Status = "failed";
            message.LatencyMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            _db.CopilotUsageEvents.Add(new CopilotUsageEvent
            {
                TenantId = tenantId,
                UserId = userId,
                RequestId = requestId,
                Feature = session.Feature,
                Mode = mode.ToString(),
                Status = "failed",
                BlockKind = kind,
                LatencyMs = message.LatencyMs,
                InputHash = message.InputHash,
            });
            await _db.SaveChangesAsync(ct);
            return new CopilotSessionResult(new CopilotSessionDto(session.Id, "failed", mode.ToString(), session.Runtime, ex.Message, null, kind, preview, message.LatencyMs), FailClosedChat(settings, requestId, kind, ex.Message), StatusCodes.Status409Conflict);
        }
    }

    public async Task<CopilotSessionDto?> CancelSessionAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await _db.CopilotSessions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.Id == sessionId, ct);
        if (session is null) return null;
        session.Status = "cancelled";
        session.CancelledAt = DateTimeOffset.UtcNow;
        session.LastErrorKind = "cancelled";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        var preview = new CopilotContextPreviewDto("", false, Array.Empty<CopilotContextItemDto>(), Array.Empty<CopilotContextRemovalDto>(), session.ContextHash);
        return new CopilotSessionDto(session.Id, session.Status, session.Mode.ToString(), session.Runtime, "Session marked cancelled.", null, "cancelled", preview, 0);
    }

    public async Task<IReadOnlyList<CopilotQuotaPolicyDto>> ListQuotaPoliciesAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await _db.CopilotQuotaPolicies.Where(x => x.TenantId == tenantId).OrderBy(x => x.ScopeType).ThenBy(x => x.Feature).ToArrayAsync(ct);
        if (rows.Length == 0) return DefaultQuotaPolicies().Select(ToDto).ToArray();
        return rows.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<CopilotQuotaPolicyDto>> SaveQuotaPoliciesAsync(Guid tenantId, IEnumerable<CopilotQuotaPolicyDto> policies, CancellationToken ct)
    {
        var rows = await _db.CopilotQuotaPolicies.Where(x => x.TenantId == tenantId).ToArrayAsync(ct);
        _db.CopilotQuotaPolicies.RemoveRange(rows);
        foreach (var dto in policies)
        {
            _db.CopilotQuotaPolicies.Add(new CopilotQuotaPolicy
            {
                TenantId = tenantId,
                ScopeType = Clean(dto.ScopeType, 40).ToLowerInvariant(),
                ScopeKey = Clean(dto.ScopeKey, 120),
                Feature = string.IsNullOrWhiteSpace(dto.Feature) ? "chat" : Clean(dto.Feature, 80).ToLowerInvariant(),
                WindowSeconds = Math.Clamp(dto.WindowSeconds, 60, 86_400),
                MaxRequests = Math.Clamp(dto.MaxRequests, 1, 100_000),
                MaxConcurrent = Math.Clamp(dto.MaxConcurrent, 1, 100),
                Enabled = dto.Enabled,
            });
        }
        await _db.SaveChangesAsync(ct);
        return await ListQuotaPoliciesAsync(tenantId, ct);
    }

    public async Task<CopilotUsageSummaryDto> UsageSummaryAsync(Guid tenantId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);
        var usage = await _db.CopilotUsageEvents.Where(x => x.TenantId == tenantId && x.CreatedAt >= since).ToArrayAsync(ct);
        var sessions = await _db.CopilotSessions.Where(x => x.TenantId == tenantId && x.Status == "running").CountAsync(ct);
        var buckets = usage.GroupBy(x => x.Status).Select(g => new CopilotUsageBucketDto(g.Key, g.Count())).OrderBy(x => x.Status).ToArray();
        return new CopilotUsageSummaryDto(
            usage.Length,
            usage.Count(x => x.Status == "completed"),
            usage.Count(x => x.Status == "blocked"),
            usage.Count(x => x.Status == "failed"),
            usage.Count(x => x.Status == "cancelled"),
            sessions,
            buckets);
    }

    public async Task<CopilotQuotaStatusDto> CheckQuotaAsync(Guid tenantId, Guid userId, string feature, CancellationToken ct)
    {
        var policies = await _db.CopilotQuotaPolicies.Where(x => x.TenantId == tenantId && x.Enabled).ToArrayAsync(ct);
        if (policies.Length == 0) policies = DefaultQuotaPolicies().ToArray();
        var applicable = policies.Where(p => AppliesTo(p, userId, feature)).ToArray();
        foreach (var policy in applicable)
        {
            var since = DateTimeOffset.UtcNow.AddSeconds(-policy.WindowSeconds);
            var query = _db.CopilotUsageEvents.Where(x => x.TenantId == tenantId && x.CreatedAt >= since && x.Status != "quota_blocked");
            if (policy.ScopeType == "user") query = query.Where(x => x.UserId == userId);
            if (policy.Feature != "*" && !string.IsNullOrWhiteSpace(policy.Feature)) query = query.Where(x => x.Feature == policy.Feature);
            var used = await query.CountAsync(ct);
            var runningQuery = _db.CopilotSessions.Where(x => x.TenantId == tenantId && x.Status == "running");
            if (policy.ScopeType == "user") runningQuery = runningQuery.Where(x => x.UserId == userId);
            if (policy.Feature != "*" && !string.IsNullOrWhiteSpace(policy.Feature)) runningQuery = runningQuery.Where(x => x.Feature == policy.Feature);
            var running = await runningQuery.CountAsync(ct);
            if (used >= policy.MaxRequests)
            {
                return new CopilotQuotaStatusDto(false, "quota_exceeded", "Copilot request quota exceeded.", policy.ScopeType, policy.ScopeKey, policy.Feature, used, policy.MaxRequests, running, policy.MaxConcurrent);
            }
            if (running >= policy.MaxConcurrent)
            {
                return new CopilotQuotaStatusDto(false, "concurrency_exceeded", "Copilot concurrency limit exceeded.", policy.ScopeType, policy.ScopeKey, policy.Feature, used, policy.MaxRequests, running, policy.MaxConcurrent);
            }
        }
        return new CopilotQuotaStatusDto(true, "ok", "Quota available.", "tenant", "", feature, 0, 0, 0, 0);
    }

    public CopilotErrorDto FailClosedChat(CopilotIntegrationSettings settings, string requestId, string? kindOverride = null, string? messageOverride = null)
    {
        var status = Status(settings);
        var kind = kindOverride ?? (status.Kind == "ready" ? "runtime_not_enabled" : status.Kind);
        return new CopilotErrorDto(
            kind,
            messageOverride ?? "Copilot chat is blocked until all official runtime, auth, context, PHI, entitlement, and quota gates pass.",
            status.RuntimeStatus,
            requestId);
    }

    public string Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public CopilotRuntimeStatus RuntimeStatus(CopilotIntegrationSettings s)
    {
        if (!s.Enabled || s.EmergencyDisabled || s.DefaultMode == CopilotMode.Disabled)
            return CopilotRuntimeStatus.Disabled;
        if (s.DefaultMode == CopilotMode.LocalCli)
            return s.CliRuntimeEnabled ? CopilotRuntimeStatus.Ready : CopilotRuntimeStatus.NotConfigured;
        if ((s.DefaultMode is CopilotMode.EnterpriseManaged or CopilotMode.BringYourOwnAccount) && !s.SdkRuntimeEnabled)
            return CopilotRuntimeStatus.NotConfigured;
        return CopilotRuntimeStatus.RuntimeUnavailable;
    }

    private async Task<CopilotSessionResult> BlockAsync(
        Guid tenantId,
        Guid userId,
        string requestId,
        CopilotSessionRequest request,
        CopilotIntegrationSettings settings,
        CopilotContextPreviewDto preview,
        string kind,
        string message,
        CancellationToken ct)
    {
        _db.CopilotUsageEvents.Add(new CopilotUsageEvent
        {
            TenantId = tenantId,
            UserId = userId,
            RequestId = requestId,
            Feature = request.ContextKind ?? "chat",
            Mode = request.Mode ?? settings.DefaultMode.ToString(),
            Status = "blocked",
            BlockKind = kind,
            InputHash = Hash(request.Message),
        });
        await _db.SaveChangesAsync(ct);
        return Error(kind, message, Status(settings).RuntimeStatus, requestId, StatusCodes.Status409Conflict, preview);
    }

    private static CopilotSessionResult Error(string kind, string message, string runtimeStatus, string requestId, int statusCode, CopilotContextPreviewDto? preview = null) =>
        new(null, new CopilotErrorDto(kind, message, runtimeStatus, requestId), statusCode);

    private async Task<string> RunLocalCliAsync(string prompt, CancellationToken ct)
    {
        var bin = ResolveCopilotBinary();
        EnforceBinaryAllowlist(bin);
        var result = await _processLauncher.RunAsync(new ProcessLaunchSpec(
            FileName: bin,
            Arguments: new[] { "copilot", "suggest", "--type", "explain" },
            StandardInput: prompt,
            TimeoutMs: ResolveTimeoutMs()), ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"gh copilot exited with code {result.ExitCode}: {Truncate(result.StandardError)}");
        return result.StandardOutput.TrimEnd();
    }

    private static string BuildPrompt(string message, IReadOnlyList<CopilotContextItemDto> context)
    {
        if (context.Count == 0) return message.Trim();
        var sb = new StringBuilder();
        sb.AppendLine("Use only this non-PHI, redacted context. Do not execute commands.");
        foreach (var item in context)
        {
            sb.AppendLine($"--- {item.Kind}: {item.Label} ---");
            sb.AppendLine(item.Text);
        }
        sb.AppendLine("--- user request ---");
        sb.AppendLine(message.Trim());
        return sb.ToString();
    }

    private static (bool allowed, string reason, string source) EvaluateEntitlement(CopilotIntegrationSettings settings, CopilotUserAccount? account, CopilotMode mode)
    {
        if (!settings.Enabled || settings.EmergencyDisabled) return (false, "copilot_disabled", "tenant_policy");
        if (!ModeAllowed(settings, mode)) return (false, "mode_not_allowed", "tenant_policy");
        if (mode == CopilotMode.Disabled) return (false, "copilot_disabled", "tenant_policy");
        if (mode == CopilotMode.BringYourOwnAccount && !settings.AllowByoAccounts) return (false, "byo_disabled", "tenant_policy");
        if (account is null || string.IsNullOrWhiteSpace(account.GitHubLogin)) return (false, "account_required", "account");
        if (string.Equals(account.TokenStatus, "revoked", StringComparison.OrdinalIgnoreCase)) return (false, "revoked", "account");
        if (mode == CopilotMode.LocalCli)
        {
            return string.Equals(account.TokenStatus, "cli_keychain", StringComparison.OrdinalIgnoreCase)
                ? (true, "", "local_cli")
                : (false, "local_cli_login_required", "local_cli");
        }
        if (string.Equals(account.SsoStatus, "blocked", StringComparison.OrdinalIgnoreCase)) return (false, "sso_required", "github");
        if (account.SeatStatus.Contains("inactive", StringComparison.OrdinalIgnoreCase) || account.SeatStatus.Contains("missing", StringComparison.OrdinalIgnoreCase))
            return (false, "seat_required", "github");
        return string.IsNullOrWhiteSpace(account.TokenSecretRef)
            ? (false, "oauth_token_required", "oauth")
            : (true, "", "oauth");
    }

    private static CopilotEntitlementDto ToEntitlementDto(CopilotEntitlement e) => new(
        e.Allowed,
        e.Mode.ToString(),
        e.Source,
        e.GitHubLogin,
        e.SsoStatus,
        e.SeatStatus,
        e.DenialReason,
        e.CheckedAt,
        e.ExpiresAt);

    private static CopilotQuotaPolicyDto ToDto(CopilotQuotaPolicy p) => new(p.Id, p.ScopeType, p.ScopeKey, p.Feature, p.WindowSeconds, p.MaxRequests, p.MaxConcurrent, p.Enabled);

    private static IEnumerable<CopilotQuotaPolicy> DefaultQuotaPolicies()
    {
        yield return new CopilotQuotaPolicy { ScopeType = "tenant", ScopeKey = "", Feature = "chat", WindowSeconds = 3600, MaxRequests = 100, MaxConcurrent = 5, Enabled = true };
        yield return new CopilotQuotaPolicy { ScopeType = "user", ScopeKey = "", Feature = "chat", WindowSeconds = 3600, MaxRequests = 20, MaxConcurrent = 1, Enabled = true };
    }

    private static bool AppliesTo(CopilotQuotaPolicy policy, Guid userId, string feature)
    {
        if (policy.ScopeType == "user" && !string.IsNullOrWhiteSpace(policy.ScopeKey) && !string.Equals(policy.ScopeKey, userId.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;
        return policy.Feature == "*" || string.Equals(policy.Feature, feature, StringComparison.OrdinalIgnoreCase) || string.Equals(policy.Feature, "chat", StringComparison.OrdinalIgnoreCase);
    }

    private CopilotMode ParseRequestedMode(string? value, CopilotIntegrationSettings settings) =>
        TryParseMode(value, out var mode) ? mode : settings.DefaultMode;

    private static bool TryParseMode(string? value, out CopilotMode mode) =>
        Enum.TryParse(value?.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal), true, out mode);

    private static bool ModeAllowed(CopilotIntegrationSettings settings, CopilotMode mode) =>
        SplitModes(settings.AllowedModes).Any(x => string.Equals(x, mode.ToString(), StringComparison.OrdinalIgnoreCase));

    private static string NormalizeMode(string? value) => TryParseMode(value, out var mode) ? mode.ToString() : "Disabled";

    private static string[] SplitModes(string? modes) => string.IsNullOrWhiteSpace(modes)
        ? new[] { CopilotMode.Disabled.ToString() }
        : modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool LooksLikeJsonObject(string? value)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static bool AllowedSecretRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("vault:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("kms:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("aws:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("azkv:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("gcp:", StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim()[..Math.Min(max, value.Trim().Length)];

    private static bool IsClinicalContext(string? value)
    {
        var v = value ?? "";
        return v.Contains("report", StringComparison.OrdinalIgnoreCase)
            || v.Contains("clinical", StringComparison.OrdinalIgnoreCase)
            || v.Contains("patient", StringComparison.OrdinalIgnoreCase)
            || v.Contains("study", StringComparison.OrdinalIgnoreCase)
            || v.Contains("phi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePhi(string? value)
    {
        var v = value ?? "";
        return v.Contains("MRN", StringComparison.OrdinalIgnoreCase)
            || v.Contains("DOB", StringComparison.OrdinalIgnoreCase)
            || v.Contains("patient name", StringComparison.OrdinalIgnoreCase)
            || v.Contains("accession", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSecret(string value)
    {
        var v = value;
        return v.Contains("ghp_", StringComparison.Ordinal)
            || v.Contains("github_pat_", StringComparison.Ordinal)
            || v.Contains("Authorization:", StringComparison.OrdinalIgnoreCase)
            || v.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || v.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || v.Contains("-----BEGIN", StringComparison.Ordinal);
    }

    private static string RedactSecrets(string value)
    {
        var lines = value.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (LooksLikeSecret(lines[i])) lines[i] = "[redacted secret-bearing line]";
        }
        return string.Join('\n', lines);
    }

    private static bool LooksBinary(string value) => value.IndexOf('\0') >= 0;

    private static bool LooksLikeExcludedPath(string label)
    {
        var lower = label.ToLowerInvariant();
        return lower.EndsWith(".lock", StringComparison.Ordinal)
            || lower.EndsWith(".png", StringComparison.Ordinal)
            || lower.EndsWith(".jpg", StringComparison.Ordinal)
            || lower.EndsWith(".jpeg", StringComparison.Ordinal)
            || lower.EndsWith(".gif", StringComparison.Ordinal)
            || lower.EndsWith(".pdf", StringComparison.Ordinal)
            || lower.Contains("node_modules", StringComparison.Ordinal)
            || lower.Contains("\\bin\\", StringComparison.Ordinal)
            || lower.Contains("\\obj\\", StringComparison.Ordinal);
    }

    private static string ResolveCopilotBinary()
    {
        var configured = Environment.GetEnvironmentVariable("RADIOPAD_GH_COPILOT_BIN");
        return string.IsNullOrWhiteSpace(configured) ? DefaultCopilotBinary : configured.Trim();
    }

    private static int ResolveTimeoutMs()
    {
        var configured = Environment.GetEnvironmentVariable("RADIOPAD_COPILOT_CLI_TIMEOUT_MS")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_TIMEOUT_MS");
        return int.TryParse(configured, out var ms) && ms > 0 ? ms : 60_000;
    }

    private static void EnforceBinaryAllowlist(string fileName)
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS");
        if (string.IsNullOrWhiteSpace(raw)) return;
        var allowed = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!allowed.Any(entry => string.Equals(entry, fileName, cmp)))
            throw new InvalidOperationException("cli_binary_not_allowed");
    }

    private static string Truncate(string s) => s.Length > 4096 ? s[..4096] : s;
}
