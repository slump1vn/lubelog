using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class UserSessionDataAccess : IUserSessionDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "user_sessions";
        public UserSessionDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        private ILiteCollection<UserSession> GetTable()
        {
            var table = _liteDB.GetLiteDB().GetCollection<UserSession>(tableName);
            table.EnsureIndex(x => x.UserId);
            return table;
        }
        //LiteDB stores dates in UTC but hands them back as local time.
        private static UserSession AsUtc(UserSession session)
        {
            session.CreatedUtc = session.CreatedUtc.ToUniversalTime();
            session.LastSeenUtc = session.LastSeenUtc.ToUniversalTime();
            session.ExpiresUtc = session.ExpiresUtc.ToUniversalTime();
            session.RevokedUtc = session.RevokedUtc?.ToUniversalTime();
            return session;
        }
        public UserSession? GetSessionById(int sessionId)
        {
            var session = GetTable().FindById(sessionId);
            return session == null ? null : AsUtc(session);
        }
        public List<UserSession> GetSessionsByUser(int userId)
        {
            return GetTable().Query()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedUtc)
                .ToList()
                .Select(AsUtc)
                .ToList();
        }
        public bool SaveSession(UserSession session)
        {
            var table = GetTable();
            bool result = session.Id == default ? table.Insert(session) != null : table.Update(session);
            _liteDB.GetLiteDB().Checkpoint();
            return result;
        }
        public int RevokeSessionsForUser(int userId, DateTime revokedUtc, int? exceptSessionId)
        {
            var table = GetTable();
            var sessions = table.Find(x => x.UserId == userId && x.RevokedUtc == null).Where(x => x.Id != exceptSessionId).ToList();
            foreach (var session in sessions)
            {
                session.RevokedUtc = revokedUtc;
                table.Update(session);
            }
            _liteDB.GetLiteDB().Checkpoint();
            return sessions.Count;
        }
        public int DeleteSessionsEndedBefore(DateTime cutoffUtc)
        {
            var result = GetTable().DeleteMany(x => x.ExpiresUtc < cutoffUtc || (x.RevokedUtc != null && x.RevokedUtc < cutoffUtc));
            _liteDB.GetLiteDB().Checkpoint();
            return result;
        }
    }
}
