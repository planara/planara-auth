namespace Planara.Auth.Registration;

public static class RegistrationRequest
{
    public const string ContextKey = "planara.registration.context";

    public const string CookieName = "__Host-planara-registration";

    public const string FlowHeader = "X-Planara-Registration-Flow";

    public const string SessionTokenHeader = "X-Planara-Registration-Token";

    public const string SessionKey = "planara.registration.session";

    public static void SetCookie(HttpContext context, string jwt, DateTime expiresAtUtc)
    {
        context.Response.Cookies.Append(CookieName, jwt,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc))
            });
    }

    public static void DeleteCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
    }
}