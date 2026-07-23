using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Services;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Server-Sent-Events stream (PR-B1) — one long-lived GET per signed-in client that
/// pushes the caller's own terminal job transitions, streamed progress/partials, and
/// notifications as they happen, replacing the jobs-widget poll.
///
/// <para>Authenticated (via <see cref="TenantedController.ResolveContextAsync"/>) but
/// with NO extra permission gate: notifications must reach every role, and job/progress/
/// partial events already self-filter to the caller's own jobs inside the bus. The
/// handler touches the DB only for that one identity resolution, so the long-lived
/// scoped context is inert afterwards (EF releases the connection after the query).</para>
///
/// <para>Not rate-limited by the "ai" policy (same doctrine as the poll endpoints); the
/// global RateLimitMiddleware counts a single hit at connect — negligible. It is also
/// exempted from PerfBudgetMiddleware's route histogram (an hours-long connection would
/// record one enormous duration sample).</para>
/// </summary>
[ApiController]
[Route("api/events")]
public sealed class EventsController : TenantedController
{
    // Manual SSE writes bypass MVC's configured JSON options, so mirror them here
    // (Program.cs: camelCase + omit-when-null) or the wire shape would diverge.
    private static readonly JsonSerializerOptions SseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RadioPadDbContext _db;
    private readonly IAiJobEventBus _bus;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _config;

    public EventsController(
        RadioPadDbContext db,
        IAiJobEventBus bus,
        IHostApplicationLifetime lifetime,
        IConfiguration config)
    {
        _db = db;
        _bus = bus;
        _lifetime = lifetime;
        _config = config;
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        // Auth only — resolve BEFORE writing any bytes so an unauthenticated request
        // surfaces the standard 401 (GlobalExceptionMiddleware) rather than an empty
        // stream. A first-party client sends real headers via fetch()+ReadableStream;
        // a webview EventSource authenticates via ?access_token= (see RadioPadBearerMiddleware).
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        // Defeat proxy (nginx) and Kestrel response buffering that would coalesce events.
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var keepAlive = TimeSpan.FromSeconds(Math.Max(1, _config.GetValue<int?>("AiJobs:SseKeepAliveSeconds") ?? 15));

        // Client abort AND graceful shutdown both end the loop; on ApplicationStopping the
        // connection closes cleanly so SSE never holds Kestrel shutdown hostage.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted, _lifetime.ApplicationStopping);
        using var sub = _bus.Subscribe(tenant.Id, user.Id);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                readTimeout.CancelAfter(keepAlive);
                try
                {
                    var evt = await sub.Reader.ReadAsync(readTimeout.Token);
                    await WriteEventAsync(evt, linked.Token);
                }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested)
                {
                    // No event within the keep-alive window — emit an SSE comment to keep
                    // idle proxies/Kestrel MinResponseDataRate from dropping the connection.
                    await Response.WriteAsync(": keep-alive\n\n", linked.Token);
                    await Response.Body.FlushAsync(linked.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client aborted or the app is stopping — clean close.
        }
        catch (Exception)
        {
            // Any write failure means the client is gone; exit and let `using sub` unsubscribe.
        }
    }

    private async Task WriteEventAsync(AiJobBusEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt.Payload, SseJson);
        await Response.WriteAsync($"event: {evt.EventType}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
