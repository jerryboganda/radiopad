using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;

namespace RadioPad.Api.Services;

/// <summary>
/// Shared Server-Sent-Events wire primitives for the app's SSE endpoints — the hosted
/// <c>GET /api/events/stream</c> (<see cref="Controllers.EventsController"/>) and the desktop
/// sidecar's loopback-only <c>GET /api/local-generation/events</c>
/// (<see cref="Controllers.LocalGenerationController"/>). Extracted so both write byte-identical
/// SSE: the same headers + buffering opt-out, the same manual camelCase + omit-when-null JSON
/// serialization (manual writes bypass MVC's configured JSON options, so they must be mirrored
/// here or the wire shape would diverge), and the same keep-alive comment shape.
/// </summary>
public static class SseWriter
{
    // Mirrors Program.cs's configured MVC JSON options (camelCase + omit-when-null); manual SSE
    // writes bypass MVC's JSON pipeline, so without this the event bodies would serialize with
    // PascalCase names and emit nulls.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Sets the standard SSE response headers and disables response buffering, so events flush to
    /// the client immediately instead of being coalesced by Kestrel or an nginx proxy.
    /// </summary>
    public static void PrepareResponse(HttpResponse response)
    {
        response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        // Defeat proxy (nginx) and Kestrel response buffering that would coalesce events.
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    /// <summary>Writes one SSE event (<c>event: {type}\ndata: {json}\n\n</c>) and flushes.</summary>
    public static async Task WriteEventAsync(HttpResponse response, string eventType, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        await response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Writes an SSE keep-alive comment (<c>: keep-alive\n\n</c>) and flushes — keeps idle proxies
    /// and Kestrel's MinResponseDataRate from dropping a quiet connection.
    /// </summary>
    public static async Task WriteKeepAliveAsync(HttpResponse response, CancellationToken ct)
    {
        await response.WriteAsync(": keep-alive\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
