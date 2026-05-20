using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace RadioPad.Api.Auth;

public static class RadioPadSessionCookies
{
    public const string CookieName = "radiopad_session";

    public static void Append(HttpResponse response, HttpRequest request, string token, DateTimeOffset expiresAt, IHostEnvironment env)
    {
        var maxAge = expiresAt - DateTimeOffset.UtcNow;
        if (maxAge < TimeSpan.Zero) maxAge = TimeSpan.Zero;

        response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction() || request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
            MaxAge = maxAge,
        });
    }

    public static void Delete(HttpResponse response, HttpRequest request, IHostEnvironment env)
    {
        response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction() || request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    public static string? ExtractBearer(HttpRequest request)
    {
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        return request.Cookies[CookieName];
    }

    public static void Clear(HttpContext ctx)
    {
        var env = ctx.RequestServices.GetService<IHostEnvironment>();
        ctx.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = env?.IsProduction() == true || ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }
}