using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// OAuth 2.0 authorization server for the native apps: authorization code with PKCE only
    /// (RFC 6749, RFC 7636, RFC 8252), token revocation (RFC 7009) and server metadata (RFC 8414).
    /// The user signs in on the regular login page inside the system browser, never inside the app.
    /// </summary>
    public class OAuthController : Controller
    {
        private readonly IOAuthLogic _oauthLogic;
        private readonly ILoginLogic _loginLogic;
        private readonly IConfigHelper _config;
        private readonly ITranslationHelper _translator;
        public OAuthController(IOAuthLogic oauthLogic, ILoginLogic loginLogic, IConfigHelper config, ITranslationHelper translator)
        {
            _oauthLogic = oauthLogic;
            _loginLogic = loginLogic;
            _config = config;
            _translator = translator;
        }
        private string T(string text) => _translator.Translate(_config.GetServerLanguage(), text);

        [HttpGet("/oauth/authorize")]
        [AllowAnonymous]
        public IActionResult Authorize(
            [FromQuery(Name = "response_type")] string? responseType,
            [FromQuery(Name = "client_id")] string? clientId,
            [FromQuery(Name = "redirect_uri")] string? redirectUri,
            [FromQuery(Name = "state")] string? state,
            [FromQuery(Name = "code_challenge")] string? codeChallenge,
            [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod)
        {
            //an unknown client or redirect gets an error page here, never a redirect (RFC 6749 section 4.1.2.1).
            var client = OAuthRules.FindClient(clientId);
            if (client == null || !OAuthRules.IsRegisteredRedirect(client, redirectUri))
            {
                return View("Authorize", new OAuthConsentViewModel { Error = T("This app is not allowed to sign in here.") });
            }
            if (responseType != "code")
            {
                return RedirectWithError(redirectUri!, state, "unsupported_response_type");
            }
            if (codeChallengeMethod != "S256" || !OAuthRules.IsWellFormedPkceValue(codeChallenge))
            {
                return RedirectWithError(redirectUri!, state, "invalid_request", "PKCE with S256 is required.");
            }
            if (!User.IsInRole("CookieAuth"))
            {
                //sign in on the regular page first, then come back here.
                var back = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Request.Path}{Request.QueryString}"));
                return Redirect($"/Login/Index?redirectURLBase64={Uri.EscapeDataString(back)}");
            }
            return View("Authorize", new OAuthConsentViewModel
            {
                ClientName = client.Name,
                UserName = User.Identity?.Name ?? string.Empty,
                ClientId = client.ClientId,
                RedirectUri = redirectUri!,
                State = state ?? string.Empty,
                CodeChallenge = codeChallenge!
            });
        }

        [HttpPost("/oauth/authorize")]
        [Authorize(Roles = "CookieAuth")]
        [ValidateAntiForgeryToken]
        public IActionResult AuthorizeDecision(
            [FromForm(Name = "client_id")] string? clientId,
            [FromForm(Name = "redirect_uri")] string? redirectUri,
            [FromForm(Name = "state")] string? state,
            [FromForm(Name = "code_challenge")] string? codeChallenge,
            [FromForm(Name = "decision")] string? decision)
        {
            var client = OAuthRules.FindClient(clientId);
            if (client == null || !OAuthRules.IsRegisteredRedirect(client, redirectUri))
            {
                return View("Authorize", new OAuthConsentViewModel { Error = T("This app is not allowed to sign in here.") });
            }
            if (!OAuthRules.IsWellFormedPkceValue(codeChallenge))
            {
                return RedirectWithError(redirectUri!, state, "invalid_request", "PKCE with S256 is required.");
            }
            if (decision != "allow")
            {
                return RedirectWithError(redirectUri!, state, "access_denied");
            }
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            var code = _oauthLogic.CreateAuthorizationCode(new OAuthAuthorizationCode
            {
                ClientId = client.ClientId,
                RedirectUri = redirectUri!,
                CodeChallenge = codeChallenge!,
                User = new UserData
                {
                    Id = userId,
                    UserName = User.Identity?.Name ?? string.Empty,
                    EmailAddress = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                    IsAdmin = User.IsInRole(nameof(UserData.IsAdmin)),
                    IsRootUser = User.IsInRole(nameof(UserData.IsRootUser))
                }
            });
            return Redirect(OAuthRules.BuildRedirect(redirectUri!, new Dictionary<string, string> { { "code", code }, { "state", state ?? string.Empty } }));
        }

        [HttpPost("/oauth/token")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting(LoginSecurity.RateLimitPolicy)]
        public IActionResult Token(
            [FromForm(Name = "grant_type")] string? grantType,
            [FromForm(Name = "client_id")] string? clientId,
            [FromForm(Name = "code")] string? code,
            [FromForm(Name = "redirect_uri")] string? redirectUri,
            [FromForm(Name = "code_verifier")] string? codeVerifier,
            [FromForm(Name = "refresh_token")] string? refreshToken,
            [FromForm(Name = "device_name")] string? deviceName)
        {
            var result = grantType switch
            {
                "authorization_code" => _oauthLogic.ExchangeCode(code, clientId, redirectUri, codeVerifier, deviceName, HttpContext),
                "refresh_token" => _oauthLogic.Refresh(refreshToken, clientId, HttpContext),
                _ => OAuthTokenResponse.Fail("unsupported_grant_type", "Use authorization_code or refresh_token.")
            };
            NoStore();
            if (!result.Succeeded)
            {
                return result.Error == "invalid_client" ? Unauthorized(result) : BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPost("/oauth/revoke")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult Revoke([FromForm(Name = "token")] string? token, [FromForm(Name = "client_id")] string? clientId)
        {
            //always 200, so the answer says nothing about whether the token existed (RFC 7009 section 2.2).
            _oauthLogic.Revoke(token, clientId);
            NoStore();
            return Ok();
        }

        [HttpGet("/.well-known/oauth-authorization-server")]
        [AllowAnonymous]
        public IActionResult Metadata()
        {
            var issuer = $"{Request.Scheme}://{Request.Host}";
            return Json(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["authorization_endpoint"] = $"{issuer}/oauth/authorize",
                ["token_endpoint"] = $"{issuer}/oauth/token",
                ["revocation_endpoint"] = $"{issuer}/oauth/revoke",
                ["response_types_supported"] = new[] { "code" },
                ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "none" },
                ["revocation_endpoint_auth_methods_supported"] = new[] { "none" }
            });
        }

        private void NoStore()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
        }
        private IActionResult RedirectWithError(string redirectUri, string? state, string error, string? description = null)
        {
            return Redirect(OAuthRules.BuildRedirect(redirectUri, new Dictionary<string, string>
            {
                { "error", error },
                { "error_description", description ?? string.Empty },
                { "state", state ?? string.Empty }
            }));
        }
    }
}
