using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;

namespace CarCareTracker.Logic
{
    public interface ISessionLogic
    {
        /// <summary>Records a new browser sign-in and returns it with the secret for the login cookie.</summary>
        (UserSession Session, string Secret) StartBrowserSession(UserData user, DateTime expiresUtc, HttpContext context);
        /// <summary>Records a new app sign-in and returns it with the secret of its first refresh token.</summary>
        (UserSession Session, string Secret) StartAppSession(UserData user, string clientId, string deviceName, DateTime expiresUtc, HttpContext context);
        /// <summary>The session behind a Bearer access token, while it is active and belongs to the user.</summary>
        UserSession? GetActiveTokenSession(int sessionId, int userId, HttpContext? context);
        /// <summary>The session when it exists, belongs to the user, matches the secret and has not ended.</summary>
        UserSession? GetActiveSession(int sessionId, int userId, string secret, HttpContext? context);
        /// <summary>Ends one session of the user. False when it is not theirs.</summary>
        bool EndSession(int userId, int sessionId);
        /// <summary>Ends every session of the user except the one given (null ends all).</summary>
        int EndOtherSessions(int userId, int? keepSessionId);
        /// <summary>Active sessions of the user for the device list, the current one first.</summary>
        List<UserSessionViewModel> GetActiveSessions(int userId, int? currentSessionId);
    }
    public class SessionLogic : ISessionLogic
    {
        private readonly IUserSessionDataAccess _sessionData;
        private readonly ILogger<SessionLogic> _logger;
        public SessionLogic(IUserSessionDataAccess sessionData, ILogger<SessionLogic> logger)
        {
            _sessionData = sessionData;
            _logger = logger;
        }
        public (UserSession Session, string Secret) StartBrowserSession(UserData user, DateTime expiresUtc, HttpContext context)
        {
            return Start(user, SessionKind.Browser, string.Empty, SessionRules.DescribeDevice(context.Request.Headers.UserAgent.ToString()), expiresUtc, context);
        }
        public (UserSession Session, string Secret) StartAppSession(UserData user, string clientId, string deviceName, DateTime expiresUtc, HttpContext context)
        {
            return Start(user, SessionKind.App, clientId, deviceName, expiresUtc, context);
        }
        private (UserSession Session, string Secret) Start(UserData user, SessionKind kind, string clientId, string deviceName, DateTime expiresUtc, HttpContext context)
        {
            var now = DateTime.UtcNow;
            _sessionData.DeleteSessionsEndedBefore(now - SessionRules.KeepEnded);
            var secret = SessionRules.NewSecret();
            var session = new UserSession
            {
                UserId = user.Id,
                UserName = user.UserName,
                EmailAddress = user.EmailAddress ?? string.Empty,
                IsAdmin = user.IsAdmin,
                IsRootUser = user.IsRootUser,
                Kind = kind,
                ClientId = clientId,
                //an app session is reached by its refresh token, a browser session by its cookie secret.
                SecretHash = kind == SessionKind.Browser ? SessionRules.Hash(secret) : string.Empty,
                RefreshTokenHash = kind == SessionKind.App ? SessionRules.Hash(secret) : string.Empty,
                DeviceName = deviceName,
                LastAddress = LoginSecurity.ClientKey(context),
                CreatedUtc = now,
                LastSeenUtc = now,
                ExpiresUtc = expiresUtc
            };
            if (!_sessionData.SaveSession(session) || session.Id == default)
            {
                throw new InvalidOperationException("The sign-in could not be recorded.");
            }
            return (session, secret);
        }
        public UserSession? GetActiveSession(int sessionId, int userId, string secret, HttpContext? context)
        {
            if (sessionId == default)
            {
                return null;
            }
            var session = _sessionData.GetSessionById(sessionId);
            if (session == null || session.Kind != SessionKind.Browser || !SessionRules.Matches(secret, session.SecretHash))
            {
                return null;
            }
            return CheckAndRecordSeen(session, userId, context);
        }
        public UserSession? GetActiveTokenSession(int sessionId, int userId, HttpContext? context)
        {
            var session = sessionId == default ? null : _sessionData.GetSessionById(sessionId);
            if (session == null || session.Kind != SessionKind.App)
            {
                return null;
            }
            return CheckAndRecordSeen(session, userId, context);
        }
        private UserSession? CheckAndRecordSeen(UserSession session, int userId, HttpContext? context)
        {
            var now = DateTime.UtcNow;
            if (session.UserId != userId || !SessionRules.IsActive(session, now))
            {
                return null;
            }
            if (SessionRules.ShouldRecordSeen(session.LastSeenUtc, now))
            {
                session.LastSeenUtc = now;
                if (context != null)
                {
                    session.LastAddress = LoginSecurity.ClientKey(context);
                }
                _sessionData.SaveSession(session);
            }
            return session;
        }
        public bool EndSession(int userId, int sessionId)
        {
            var session = _sessionData.GetSessionById(sessionId);
            if (session == null || session.UserId != userId)
            {
                return false;
            }
            if (session.RevokedUtc == null)
            {
                session.RevokedUtc = DateTime.UtcNow;
                _sessionData.SaveSession(session);
            }
            return true;
        }
        public int EndOtherSessions(int userId, int? keepSessionId)
        {
            var count = _sessionData.RevokeSessionsForUser(userId, DateTime.UtcNow, keepSessionId);
            if (count > 0)
            {
                _logger.LogInformation($"Signed out {count} session(s) of user {userId}.");
            }
            return count;
        }
        public List<UserSessionViewModel> GetActiveSessions(int userId, int? currentSessionId)
        {
            var now = DateTime.UtcNow;
            return _sessionData.GetSessionsByUser(userId)
                .Where(x => SessionRules.IsActive(x, now))
                .Select(x => new UserSessionViewModel
                {
                    Id = x.Id,
                    Kind = x.Kind,
                    DeviceName = x.DeviceName,
                    LastAddress = x.LastAddress,
                    CreatedUtc = x.CreatedUtc,
                    LastSeenUtc = x.LastSeenUtc,
                    ExpiresUtc = x.ExpiresUtc,
                    IsCurrent = x.Id == currentSessionId
                })
                .OrderByDescending(x => x.IsCurrent)
                .ThenByDescending(x => x.LastSeenUtc)
                .ToList();
        }
    }
}
