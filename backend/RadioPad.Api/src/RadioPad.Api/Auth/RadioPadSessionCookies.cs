namespace RadioPad.Api.Auth;

internal static class RadioPadSessionCookies
{
    public const string BearerCookieName = "rp_session";

    public static string? ExtractBearer(HttpRequest request)
    {
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }

        return request.Cookies.TryGetValue(BearerCookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken)
            ? cookieToken.Trim()
            : null;
    }

    public static void Append(HttpContext context, string bearerToken, DateTimeOffset expiresAt)
    {
        context.Response.Cookies.Append(BearerCookieName, bearerToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookies(context),
            Path = "/",
            Expires = expiresAt,
        });
    }

    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(BearerCookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookies(context),
            Path = "/",
        });
    }

    private static bool ShouldUseSecureCookies(HttpContext context)
    {
        if (context.Request.IsHttps)
            return true;

        var env = context.RequestServices.GetService<IWebHostEnvironment>();
        return env is null
            || (!env.IsDevelopment() && !env.IsEnvironment("Testing"));
    }
}
