using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class AssistantDataAccess : IAssistantDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "assistant_messages";
        public AssistantDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        private ILiteCollection<AssistantMessage> Table()
        {
            var table = _liteDB.GetLiteDB().GetCollection<AssistantMessage>(tableName);
            table.EnsureIndex(x => x.UserId);
            table.EnsureIndex(x => x.CreatedAtUtc);
            return table;
        }
        //LiteDB stores dates in UTC but hands them back as local time.
        private static AssistantMessage AsUtc(AssistantMessage message)
        {
            message.CreatedAtUtc = message.CreatedAtUtc.ToUniversalTime();
            return message;
        }
        public int Add(AssistantMessage message)
        {
            message.Id = 0;
            Table().Insert(message);
            _liteDB.GetLiteDB().Checkpoint();
            return message.Id;
        }
        public List<AssistantMessage> GetPage(int userId, int beforeId, int limit)
        {
            var query = Table().Query().Where(x => x.UserId == userId);
            if (beforeId > 0)
            {
                query = query.Where(x => x.Id < beforeId);
            }
            return query.OrderByDescending(x => x.Id).Limit(limit).ToList().Select(AsUtc).ToList();
        }
        public int DeleteAll(int userId)
        {
            var deleted = Table().DeleteMany(x => x.UserId == userId);
            _liteDB.GetLiteDB().Checkpoint();
            return deleted;
        }
        public int DeleteOlderThan(DateTime cutoffUtc)
        {
            var deleted = Table().DeleteMany(x => x.CreatedAtUtc < cutoffUtc);
            if (deleted > 0)
            {
                _liteDB.GetLiteDB().Checkpoint();
            }
            return deleted;
        }
    }
}
