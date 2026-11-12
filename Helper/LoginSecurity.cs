using Microsoft.AspNetCore.Http;

namespace CarCareTracker.Helper
{
    /// <summary>
    /// Settings that protect the sign-in: how the login cookie is sent, and how many sign-in
    /// attempts one address may make (OWASP Session Management and Authentication cheat sheets).
    /// </summary>
    public static class LoginSecurity
    {
        /// <summary>Name of the rate limiting policy on the sign-in and password reset endpoints.</summary>
        public const string RateLimitPolicy = "login";
        /// <summary>Attempts allowed per client address in one window.</summary>
        public const int AttemptsPerWindow = 10;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Options for the login cookie: never readable from JavaScript, only sent over HTTPS when the
        /// site is reached over HTTPS (behind the reverse proxy this needs forwarded headers on), and not
        /// sent on requests started by other sites, except plain top-level links.
        /// </summary>
        public static CookieOptions CookieOptions(HttpRequest request, DateTime? expires)
        {
            var options = new CookieOptions
            {
                HttpOnly = true,
                Secure = request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                IsEssential = true
            };
            if (expires.HasValue)
            {
                options.Expires = new DateTimeOffset(expires.Value);
            }
            return options;
        }

        /// <summary>The address one rate limit bucket belongs to.</summary>
        public static string ClientKey(HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
