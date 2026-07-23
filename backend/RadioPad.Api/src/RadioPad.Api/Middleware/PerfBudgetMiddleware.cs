using System.Diagnostics;
using RadioPad.Api.Auth;
using RadioPad.Api.Services;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Iter-33 PERF-004 — records per-route HTTP request duration on the
/// <c>radiopad.api.request.duration_ms</c> histogram. Tags:
/// <c>route</c> (route template, falls back to <c>path</c> when no
/// route was matched), <c>tenant</c> (verified identity or <c>(none)</c>),
/// <c>status</c> (HTTP status code as string).
/// </summary>
public sealed class PerfBudgetMiddleware
{
    private readonly RequestDelegate _next;

    public PerfBudgetMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // The SSE stream (PR-B1) is a single request held open for hours; recording its
        // duration would drop one enormous sample into the route histogram and skew any
        // global P95 rollup. Skip instrumentation for it — it never resembles a normal request.
        if (ctx.Request.Path.StartsWithSegments("/api/events/stream"))
        {
            await _next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(ctx);
        }
        finally
        {
            sw.Stop();
            var route = ctx.GetEndpoint()?.Metadata
                .GetMetadata<Microsoft.AspNetCore.Routing.RouteNameMetadata>()?.RouteName;
            if (string.IsNullOrEmpty(route))
            {
                route = (ctx.GetRouteData().Values["controller"] is string c
                    && ctx.GetRouteData().Values["action"] is string a)
                    ? $"{c}/{a}"
                    : ctx.Request.Path.Value ?? "(unknown)";
            }
            var tenant = RadioPadRequestIdentity.TenantSlugOrDevHeader(ctx);
            if (string.IsNullOrEmpty(tenant)) tenant = "(none)";
            PerfBudgets.ApiRequestDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("tenant", tenant),
                new KeyValuePair<string, object?>("status", ctx.Response.StatusCode.ToString()));
        }
    }
}
