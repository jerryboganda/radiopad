using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Public version-check for the mobile companion's "Check for updates". A
/// sideloaded Android APK can't self-update (that's the desktop's Tauri path), so
/// the phone asks the backend "what's the latest release?"; the backend queries
/// GitHub server-side and CACHES it, then returns the version + APK download URL.
///
/// Public + unauthenticated by design: the phone may not be paired/signed in when
/// it checks, and this exposes only public release metadata. Doing the lookup
/// server-side avoids the GitHub API's 60/hr per-IP anonymous limit (a real risk
/// behind a shared hospital NAT) — every phone hits this cached endpoint, not
/// GitHub — and reuses the CORS the mobile origin already has.
/// </summary>
[ApiController]
[Route("api/mobile")]
public class MobileController : ControllerBase
{
    private const string Repo = "jerryboganda/radiopad";
    private const string ApkAsset = "RadioPad-companion-android.apk";
    private const string CacheKey = "mobile.latest.release";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;

    public MobileController(IHttpClientFactory http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public record LatestMobile(string Version, string? ApkUrl, string ReleaseUrl);

    /// <summary>Latest companion version + APK URL (cached 30 min). 503 if the
    /// upstream lookup is currently unavailable — the client degrades gracefully.</summary>
    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out LatestMobile? cached) && cached is not null)
            return Ok(cached);

        var fresh = await FetchLatestAsync(ct);
        if (fresh is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Could not determine the latest version right now." });

        // Only cache SUCCESS — a transient GitHub failure must not pin a bad answer.
        _cache.Set(CacheKey, fresh, CacheTtl);
        return Ok(fresh);
    }

    private async Task<LatestMobile?> FetchLatestAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            // GitHub rejects requests without a User-Agent.
            req.Headers.UserAgent.ParseAdd("RadioPad-mobile-update-check");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var version = tag.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(version)) return null;

            string? apkUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && n.GetString() == ApkAsset
                        && a.TryGetProperty("browser_download_url", out var u))
                    {
                        apkUrl = u.GetString();
                        break;
                    }
                }
            }

            return new LatestMobile(version, apkUrl, $"https://github.com/{Repo}/releases/latest");
        }
        catch
        {
            return null;
        }
    }
}
