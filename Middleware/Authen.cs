using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CarCareTracker.Middleware
{
    public class Authen : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private IHttpContextAccessor _httpContext;
        private IDataProtector _dataProtector;
        private ILoginLogic _loginLogic;
        private ISessionLogic _sessionLogic;
        private IOAuthLogic _oauthLogic;
        private bool enableAuth;
        public Authen(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            UrlEncoder encoder,
            ILoggerFactory logger,
            IConfiguration configuration,
            ILoginLogic loginLogic,
            ISessionLogic sessionLogic,
            IOAuthLogic oauthLogic,
            IDataProtectionProvider securityProvider,
            IHttpContextAccessor httpContext) : base(options, logger, encoder)
        {
            _httpContext = httpContext;
            _dataProtector = securityProvider.CreateProtector("login");
            _loginLogic = loginLogic;
            _sessionLogic = sessionLogic;
            _oauthLogic = oauthLogic;
            enableAuth = bool.Parse(configuration["EnableAuth"] ?? "false");
        }
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!enableAuth)
            {
                //generate a fake user ticket to go with it lol.
                var appIdentity = new ClaimsIdentity("Custom");
                var userIdentity = new List<Claim>
                {
                    new(ClaimTypes.Name, "admin"),
                    new(ClaimTypes.NameIdentifier, (-1).ToString()),
                    new(ClaimTypes.Role, nameof(UserData.IsRootUser))
                };
                appIdentity.AddClaims(userIdentity);
                AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(appIdentity), Scheme.Name);
                return AuthenticateResult.Success(ticket);
            }
            else
            {
                //auth is enabled by user, we will have to authenticate the user via a ticket retrieved from the auth cookie.
                var access_token = _httpContext.HttpContext?.Request.Cookies[StaticHelper.LoginCookieName] ?? string.Empty;
                //auth using Basic Auth for API.
                var request_header = _httpContext.HttpContext?.Request.Headers["Authorization"] ?? string.Empty;
                //auth using API Key for API.
                var apikey_header = _httpContext.HttpContext?.Request.Headers["x-api-key"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(apikey_header))
                {
                    apikey_header = _httpContext.HttpContext?.Request.Query["apiKey"] ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(access_token) && string.IsNullOrWhiteSpace(request_header) && string.IsNullOrWhiteSpace(apikey_header))
                {
                    return AuthenticateResult.Fail("Cookie is invalid or does not exist.");
                }
                else if (request_header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    //access token from the app's OAuth sign-in (RFC 6750).
                    return AuthenticateBearer(request_header.ToString()["Bearer ".Length..].Trim());
                }
                else if (!string.IsNullOrWhiteSpace(request_header))
                {
                    var cleanedHeader = request_header.ToString().Replace("Basic ", "").Trim();
                    string decodedString;
                    try
                    {
                        decodedString = Encoding.UTF8.GetString(Convert.FromBase64String(cleanedHeader));
                    }
                    catch (FormatException)
                    {
                        return AuthenticateResult.Fail("Invalid credentials");
                    }
                    var splitString = decodedString.Split(":");
                    if (splitString.Count() != 2)
                    {
                        return AuthenticateResult.Fail("Invalid credentials");
                    }
                    else
                    {
                        var userData = _loginLogic.ValidateUserCredentials(new LoginModel { UserName = splitString[0], Password = splitString[1] });
                        if (userData.Id != default)
                        {
                            var appIdentity = new ClaimsIdentity("Custom");
                            var userIdentity = new List<Claim>
                            {
                                new(ClaimTypes.Name, splitString[0]),
                                new(ClaimTypes.NameIdentifier, userData.Id.ToString()),
                                new(ClaimTypes.Email, userData.EmailAddress ?? string.Empty),
                                new(ClaimTypes.Role, "APIAuth")
                            };
                            if (userData.IsAdmin)
                            {
                                userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsAdmin)));
                            }
                            if (userData.IsRootUser)
                            {
                                userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsRootUser)));
                            }
                            appIdentity.AddClaims(userIdentity);
                            AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(appIdentity), Scheme.Name);
                            return AuthenticateResult.Success(ticket);
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(access_token))
                {
                    try
                    {
                        //decrypt the access token.
                        var decryptedCookie = _dataProtector.Unprotect(access_token);
                        AuthCookie? authCookie = JsonSerializer.Deserialize<AuthCookie>(decryptedCookie);
                        if (authCookie != null)
                        {
                            //validate auth cookie
                            if (authCookie.ExpiresOn < DateTime.Now)
                            {
                                //if cookie is expired
                                return AuthenticateResult.Fail("Expired credentials");
                            }
                            else if (authCookie.UserData is null || authCookie.UserData.Id == default || string.IsNullOrWhiteSpace(authCookie.UserData.UserName))
                            {
                                return AuthenticateResult.Fail("Corrupted credentials");
                            }
                            else
                            {
                                if (!_loginLogic.CheckIfUserIsValid(authCookie.UserData.Id))
                                {
                                    return AuthenticateResult.Fail("Cookie points to non-existant user.");
                                }
                                //the cookie must belong to a session the server still knows, so that a device can be signed out.
                                var session = _sessionLogic.GetActiveSession(authCookie.SessionId, authCookie.UserData.Id, authCookie.SessionSecret, _httpContext.HttpContext);
                                if (session == null)
                                {
                                    return AuthenticateResult.Fail("Session ended.");
                                }
                                //validate if user is still valid
                                var appIdentity = new ClaimsIdentity("Custom");
                                var userIdentity = new List<Claim>
                                {
                                    new(ClaimTypes.Name, authCookie.UserData.UserName),
                                    new(ClaimTypes.NameIdentifier, authCookie.UserData.Id.ToString()),
                                    new(ClaimTypes.Email, authCookie.UserData.EmailAddress ?? string.Empty),
                                    new(ClaimTypes.Role, "CookieAuth"),
                                    new(LoginSecurity.SessionClaim, session.Id.ToString())
                                };
                                if (authCookie.UserData.IsAdmin)
                                {
                                    userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsAdmin)));
                                }
                                if (authCookie.UserData.IsRootUser)
                                {
                                    userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsRootUser)));
                                }
                                appIdentity.AddClaims(userIdentity);
                                AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(appIdentity), Scheme.Name);
                                return AuthenticateResult.Success(ticket);
                            }
                        }
                    }
                    catch
                    {
                        return AuthenticateResult.Fail("Corrupted credentials");
                    }
                } 
                else if (!string.IsNullOrWhiteSpace(apikey_header) && _httpContext.HttpContext != null && StaticHelper.GetPathAllowAPIKeyAuth(_httpContext.HttpContext.Request.Path))
                {
                    //only do API Key Auth for API methods
                    var userData = _loginLogic.ValidateAPIKey(apikey_header);
                    if (userData.Id != default)
                    {
                        var appIdentity = new ClaimsIdentity("Custom");
                        var userIdentity = new List<Claim>
                            {
                                new(ClaimTypes.Name, userData.UserName),
                                new(ClaimTypes.NameIdentifier, userData.Id.ToString()),
                                new(ClaimTypes.Email, userData.EmailAddress ?? string.Empty),
                                new(ClaimTypes.Role, "APIAuth"),
                                new(ClaimTypes.Role, "APIKeyAuth")
                            };
                        if (userData.IsAdmin)
                        {
                            userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsAdmin)));
                        }
                        if (userData.IsRootUser)
                        {
                            userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsRootUser)));
                        }
                        appIdentity.AddClaims(userIdentity);
                        AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(appIdentity), Scheme.Name);
                        return AuthenticateResult.Success(ticket);
                    }
                }
                return AuthenticateResult.Fail("Invalid credentials");
            }
        }
        private AuthenticateResult AuthenticateBearer(string accessToken)
        {
            var context = _httpContext.HttpContext;
            //access tokens are only for the API, never for the web pages.
            if (context == null || !context.Request.Path.StartsWithSegments("/api"))
            {
                return AuthenticateResult.Fail("Access tokens only work on the API.");
            }
            var token = _oauthLogic.ReadAccessToken(accessToken);
            if (token == null)
            {
                return AuthenticateResult.Fail("Invalid or expired access token.");
            }
            var session = _sessionLogic.GetActiveTokenSession(token.SessionId, token.UserId, context);
            if (session == null || !_loginLogic.CheckIfUserIsValid(session.UserId))
            {
                return AuthenticateResult.Fail("Session ended.");
            }
            var appIdentity = new ClaimsIdentity("Custom");
            var userIdentity = new List<Claim>
            {
                new(ClaimTypes.Name, session.UserName),
                new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
                new(ClaimTypes.Email, session.EmailAddress ?? string.Empty),
                new(ClaimTypes.Role, "APIAuth"),
                new(ClaimTypes.Role, "TokenAuth"),
                new(LoginSecurity.SessionClaim, session.Id.ToString())
            };
            if (session.IsAdmin)
            {
                userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsAdmin)));
            }
            if (session.IsRootUser)
            {
                userIdentity.Add(new(ClaimTypes.Role, nameof(UserData.IsRootUser)));
            }
            appIdentity.AddClaims(userIdentity);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(appIdentity), Scheme.Name));
        }
        private bool IsApiRequest()
        {
            return (Request.RouteValues.TryGetValue("controller", out object? value) && value?.ToString()?.ToLower() == "api") || Request.Path.StartsWithSegments("/api");
        }
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (IsApiRequest())
            {
                Response.StatusCode = 401;
                if (Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Headers.Append("WWW-Authenticate", "Bearer error=\"invalid_token\"");
                }
                else
                {
                    Response.Headers.Append("WWW-Authenticate", Request.Path.StartsWithSegments("/api/v1") ? "Bearer" : "Basic");
                }
                return Task.CompletedTask;
            }
            if (Request.Path.Value == "/Vehicle/Index" && Request.QueryString.HasValue)
            {
                var encodedRedirectUrl = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Request.Path.Value}{Request.QueryString.Value}"));
                Response.Redirect($"/Login/Index?redirectURLBase64={encodedRedirectUrl}");
            } else
            {
                Response.Redirect("/Login/Index");
            }
            return Task.CompletedTask;
        }
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            if (IsApiRequest())
            {
                Response.StatusCode = 403;
                return Task.CompletedTask;
            }
            Response.Redirect("/Error/403");
            return Task.CompletedTask;
        }
    }
}
