using Microsoft.AspNetCore.Http;

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
}