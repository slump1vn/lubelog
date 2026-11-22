using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace CarCareTracker.Logic
{
    public interface IOAuthLogic
    {
        /// <summary>Stores the consent for a minute and returns the one-time code for the redirect.</summary>
        string CreateAuthorizationCode(OAuthAuthorizationCode details);
        /// <summary>grant_type=authorization_code: checks the code, client, redirect and PKCE, then starts an app session.</summary>
        OAuthTokenResponse ExchangeCode(string? code, string? clientId, string? redirectUri, string? codeVerifier, string? deviceName, HttpContext context);
        /// <summary>grant_type=refresh_token: rotates the refresh token. A token used twice ends the session.</summary>
        OAuthTokenResponse Refresh(string? refreshToken, string? clientId, HttpContext context);
        /// <summary>Ends the session of a refresh token (RFC 7009). Unknown tokens are ignored.</summary>
        void Revoke(string? token, string? clientId);
        /// <summary>The content of a Bearer access token, or null when it is forged, altered or expired.</summary>
        OAuthAccessToken? ReadAccessToken(string? token);
    }
    public class OAuthLogic : IOAuthLogic
    {
        private readonly IMemoryCache _cache;
        private readonly ITimeLimitedDataProtector _accessTokenProtector;
        private readonly IUserSessionDataAccess _sessionData;
        private readonly ISessionLogic _sessionLogic;
        private readonly IConfigHelper _config;
        private readonly ILogger<OAuthLogic> _logger;
        private static readonly object codeLock = new object();
        private static readonly object refreshLock = new object();
        public OAuthLogic(IMemoryCache cache, IDataProtectionProvider dataProtection, IUserSessionDataAccess sessionData, ISessionLogic sessionLogic, IConfigHelper config, ILogger<OAuthLogic> logger)
        {
            _cache = cache;
            _accessTokenProtector = dataProtection.CreateProtector("oauth-access-token").ToTimeLimitedDataProtector();
            _sessionData = sessionData;
            _sessionLogic = sessionLogic;
            _config = config;
            _logger = logger;
        }
        private static string CodeKey(string code) => $"oauth_code_{SessionRules.Hash(code)}";
        public string CreateAuthorizationCode(OAuthAuthorizationCode details)
        {
            var code = SessionRules.NewSecret();
            _cache.Set(CodeKey(code), details, OAuthRules.CodeLifetime);
            return code;
        }
        public OAuthTokenResponse ExchangeCode(string? code, string? clientId, string? redirectUri, string? codeVerifier, string? deviceName, HttpContext context)
        {
            var client = OAuthRules.FindClient(clientId);
            if (client == null)
            {
                return OAuthTokenResponse.Fail("invalid_client", "Unknown client.");
            }
            if (string.IsNullOrEmpty(code))
            {
                return OAuthTokenResponse.Fail("invalid_request", "The code is missing.");
            }
            OAuthAuthorizationCode? details;
            //a code works once: it is taken out before anything else is checked.
            lock (codeLock)
            {
                if (_cache.TryGetValue(CodeKey(code), out details))
                {
                    _cache.Remove(CodeKey(code));
                }
            }
            if (details == null || details.ClientId != client.ClientId || details.RedirectUri != redirectUri)
            {
                return OAuthTokenResponse.Fail("invalid_grant", "The code is invalid, expired or already used.");
            }
            if (!OAuthRules.VerifyPkce(codeVerifier, details.CodeChallenge))
            {
                return OAuthTokenResponse.Fail("invalid_grant", "The code_verifier does not match.");
            }
            var name = CleanDeviceName(deviceName);
            var (session, secret) = _sessionLogic.StartAppSession(details.User, client.ClientId, string.IsNullOrEmpty(name) ? client.Name : name, NewExpiry(), context);
            _logger.LogInformation($"App sign-in for user {session.UserId} from {client.ClientId}.");
            return Issue(session, secret);
        }
        public OAuthTokenResponse Refresh(string? refreshToken, string? clientId, HttpContext context)
        {
            var client = OAuthRules.FindClient(clientId);
            if (client == null)
            {
                return OAuthTokenResponse.Fail("invalid_client", "Unknown client.");
            }
            if (!OAuthRules.TryParseRefreshToken(refreshToken, out int sessionId, out string secret))
            {
                return OAuthTokenResponse.Fail("invalid_grant", "The refresh token is invalid.");
            }
            lock (refreshLock)
            {
                var session = _sessionData.GetSessionById(sessionId);
                var now = DateTime.UtcNow;
                if (session == null || session.Kind != SessionKind.App || session.ClientId != client.ClientId || !SessionRules.IsActive(session, now))
                {
                    return OAuthTokenResponse.Fail("invalid_grant", "The refresh token is invalid.");
                }
                if (!SessionRules.Matches(secret, session.RefreshTokenHash))
                {
                    if (SessionRules.Matches(secret, session.PreviousRefreshTokenHash))
                    {
                        //an old token came back: someone holds a copy, so the session ends (RFC 9700 section 4.14.2).
                        session.RevokedUtc = now;
                        _sessionData.SaveSession(session);
                        _logger.LogWarning($"Refresh token reused for session {session.Id} of user {session.UserId}; the session was ended.");
                    }
                    return OAuthTokenResponse.Fail("invalid_grant", "The refresh token is invalid.");
                }
                var newSecret = SessionRules.NewSecret();
                session.PreviousRefreshTokenHash = session.RefreshTokenHash;
                session.RefreshTokenHash = SessionRules.Hash(newSecret);
                session.LastSeenUtc = now;
                session.LastAddress = LoginSecurity.ClientKey(context);
                //an app in use stays signed in; one left unused for the whole lifespan signs in again.
                session.ExpiresUtc = NewExpiry();
                if (!_sessionData.SaveSession(session))
                {
                    return OAuthTokenResponse.Fail("server_error", "The sign-in could not be saved.");
                }
                return Issue(session, newSecret);
            }
        }
        public void Revoke(string? token, string? clientId)
        {
            var client = OAuthRules.FindClient(clientId);
            if (client == null || !OAuthRules.TryParseRefreshToken(token, out int sessionId, out string secret))
            {
                return;
            }
            var session = _sessionData.GetSessionById(sessionId);
            if (session != null && session.Kind == SessionKind.App && session.ClientId == client.ClientId
                && (SessionRules.Matches(secret, session.RefreshTokenHash) || SessionRules.Matches(secret, session.PreviousRefreshTokenHash)))
            {
                _sessionLogic.EndSession(session.UserId, session.Id);
            }
        }
        public OAuthAccessToken? ReadAccessToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }
            try
            {
                var payload = JsonSerializer.Deserialize<OAuthAccessToken>(_accessTokenProtector.Unprotect(token));
                return payload == null || payload.SessionId == default ? null : payload;
            }
            catch
            {
                return null;
            }
        }
        private OAuthTokenResponse Issue(UserSession session, string refreshSecret)
        {
            var payload = JsonSerializer.Serialize(new OAuthAccessToken { SessionId = session.Id, UserId = session.UserId });
            return new OAuthTokenResponse
            {
                AccessToken = _accessTokenProtector.Protect(payload, OAuthRules.AccessTokenLifetime),
                TokenType = "Bearer",
                ExpiresIn = (int)OAuthRules.AccessTokenLifetime.TotalSeconds,
                RefreshToken = OAuthRules.FormatRefreshToken(session.Id, refreshSecret)
            };
        }
        private DateTime NewExpiry()
        {
            return DateTime.UtcNow.AddDays(_config.GetAuthCookieLifeSpan());
        }
        /// <summary>The name the app gives for the device list: printable characters only, at most 60.</summary>
        public static string CleanDeviceName(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return string.Empty;
            }
            var clean = new string(deviceName.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return clean.Length > 60 ? clean[..60] : clean;
        }
    }
}
