using System.Net;
using System.Text.Json;
using RadioPad.Application.Services;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Catches uncaught exceptions and produces a problem+json response without
/// leaking secrets or stack traces. <see cref="ProviderPolicyException"/> is
/// surfaced as <c>409 Conflict</c> so the frontend can show a banner that
/// explains why the AI call was refused — the policy decision itself is
/// recorded in the audit log by the gateway.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _log;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (ProviderPolicyException pp)
        {
            _log.LogWarning(pp, "Provider policy rejected request {Path}", ctx.Request.Path);
            await Write(ctx, HttpStatusCode.Conflict, "policy/provider", pp.Message);
        }
        catch (ProviderTransportException pt)
        {
            // 2026-07-11 UBAG hardening — a provider being down/slow/broken is
            // NOT an internal server error. 502 + kind=provider_transport lets
            // the frontend show "AI provider unavailable — retry / pick
            // another provider" instead of a generic failure, on EVERY AI
            // endpoint (previously only endpoints with local handling did).
            _log.LogWarning(pt, "Provider transport failure on {Path} (status={Status})",
                ctx.Request.Path, pt.StatusCode);
            await Write(ctx, HttpStatusCode.BadGateway, "provider/transport",
                $"AI provider transport failure: {pt.Message}. " +
                "The provider may be down or logged out; auto-routed requests already tried the failover chain.");
        }
        catch (QuotaExceededException qx)
        {
            // PRD BILL-001..006 — plan quota breached; map to RFC-7807 / 402
            // Payment Required so the frontend can prompt for an upgrade.
            _log.LogWarning("Plan quota rejected request {Path}: {Reason}", ctx.Request.Path, qx.Reason);
            if (ctx.Response.HasStarted) return;
            ctx.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://radiopad.app/problems/quota-exceeded",
                title = "Plan quota exceeded",
                status = StatusCodes.Status402PaymentRequired,
                kind = "quota_exceeded",
                reason = qx.Reason,
                detail = qx.Detail,
                requestId = ctx.Response.Headers[RequestCorrelationMiddleware.Header].ToString(),
            }));
        }
        catch (UnauthorizedAccessException ua)
        {
            await Write(ctx, HttpStatusCode.Unauthorized, "auth/unauthorized", ua.Message);
        }
        catch (KeyNotFoundException kn)
        {
            await Write(ctx, HttpStatusCode.NotFound, "resource/not-found", kn.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
            await Write(ctx, HttpStatusCode.InternalServerError, "internal/unhandled",
                "Internal server error. Check the request id for the corresponding log line.");
        }
    }

    private static async Task Write(HttpContext ctx, HttpStatusCode status, string type, string message)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type,
            status = (int)status,
            title = status.ToString(),
            detail = message,
            requestId = ctx.Response.Headers[RequestCorrelationMiddleware.Header].ToString(),
        }));
    }
}
