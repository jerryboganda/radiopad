using System.Diagnostics;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Stamps every request with an <c>X-RadioPad-RequestId</c> response header
/// (echoes the inbound value if present) and pushes it into the logging
/// scope so all log lines for the request can be correlated.
/// </summary>
public class RequestCorrelationMiddleware
{
    public const string Header = "X-RadioPad-RequestId";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCorrelationMiddleware> _log;

    public RequestCorrelationMiddleware(RequestDelegate next, ILogger<RequestCorrelationMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var rid = ctx.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(rid)) rid = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        ctx.TraceIdentifier = rid;
        ctx.Response.Headers[Header] = rid;

        var tenant = ctx.Request.Headers["X-RadioPad-Tenant"].ToString();
        using (_log.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = rid,
            ["Tenant"] = string.IsNullOrEmpty(tenant) ? "(none)" : tenant,
        }))
        {
            await _next(ctx);
        }
    }
}
