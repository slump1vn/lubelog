using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    public interface IUserSessionDataAccess
    {
        UserSession? GetSessionById(int sessionId);
        /// <summary>All sessions of the user, newest first, ended ones included.</summary>
        List<UserSession> GetSessionsByUser(int userId);
        /// <summary>Adds the session when its Id is 0, otherwise replaces it. Sets the Id on insert.</summary>
        bool SaveSession(UserSession session);
        /// <summary>Ends every active session of the user except the one given. Returns how many ended.</summary>
        int RevokeSessionsForUser(int userId, DateTime revokedUtc, int? exceptSessionId);
        /// <summary>Deletes sessions that expired or were ended before the cutoff.</summary>
        int DeleteSessionsEndedBefore(DateTime cutoffUtc);
    }
}
