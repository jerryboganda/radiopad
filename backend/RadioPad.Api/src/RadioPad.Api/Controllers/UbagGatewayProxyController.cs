using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Desktop → UBAG gateway passthrough.
///
/// The bundled desktop sidecar runs on a clinician's machine and cannot reach
/// the internal UBAG gateway (it lives only on the VPS Docker network). Instead
/// of shipping the real UBAG secret to every desktop, the desktop's UbagClient
/// points <c>RADIOPAD_UBAG_BASE_URL</c> at this endpoint and authenticates with a
/// dedicated, rotatable desktop proxy token (<c>RADIOPAD_DESKTOP_PROXY_TOKEN</c>).
/// We validate that token and forward to the internal gateway injecting the real
/// <c>RADIOPAD_UBAG_AUTH_SECRET</c> server-side — the real secret never leaves the
/// VPS. Restricted to the gateway paths the client actually uses.
///
/// Auth note: the proxy token MUST NOT start with "rp_" so RadioPadBearerMiddleware
/// passes it through untouched (it only validates rp_-prefixed bearers).
/// </summary>
[ApiController]
[Route("api/ubag-gw")]
public sealed class UbagGatewayProxyController : ControllerBase
{
    private static readonly string[] AllowedPrefixes = { "v1/health", "v1/targets", "v1/browser", "v1/jobs", "v1/workflows" };
    private readonly IHttpClientFactory _http;

    public UbagGatewayProxyController(IHttpClientFactory http) => _http = http;

    [Route("{**path}")]
    [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH")]
    public async Task<IActionResult> Proxy(string? path, CancellationToken ct)
    {
        var expected = Environment.GetEnvironmentVariable("RADIOPAD_DESKTOP_PROXY_TOKEN");
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Desktop UBAG proxy is not configured.", kind = "proxy_unconfigured" });

        var auth = Request.Headers.Authorization.ToString();
        var presented = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : string.Empty;
        if (presented.Length == 0 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected)))
            return Unauthorized(new { error = "Invalid desktop proxy token.", kind = "unauthenticated" });

        var p = (path ?? string.Empty).TrimStart('/');
        if (!AllowedPrefixes.Any(a => p.Equals(a, StringComparison.Ordinal) || p.StartsWith(a + "/", StringComparison.Ordinal)))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Path not permitted via the desktop UBAG proxy.", kind = "forbidden", path = p });

        var baseUrl = (Environment.GetEnvironmentVariable("RADIOPAD_UBAG_BASE_URL") ?? string.Empty).TrimEnd('/');
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_UBAG_AUTH_SECRET");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "UBAG gateway is not configured on the server.", kind = "ubag_unconfigured" });

        var target = $"{baseUrl}/{p}{Request.QueryString}";
        using var fwd = new HttpRequestMessage(new HttpMethod(Request.Method), target);
        if (Request.ContentLength is > 0)
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            fwd.Content = new ByteArrayContent(ms.ToArray());
            if (!string.IsNullOrEmpty(Request.ContentType))
                fwd.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }
        fwd.Headers.TryAddWithoutValidation("Authorization", $"Bearer {secret}");
        foreach (var h in new[] { "Idempotency-Key", "Ubag-Api-Version" })
            if (Request.Headers.TryGetValue(h, out var v))
                fwd.Headers.TryAddWithoutValidation(h, v.ToString());

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(125_000);
        using var resp = await client.SendAsync(fwd, HttpCompletionOption.ResponseHeadersRead, ct);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        Response.StatusCode = (int)resp.StatusCode;
        Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        await Response.Body.WriteAsync(bytes, ct);
        return new EmptyResult();
    }
}
