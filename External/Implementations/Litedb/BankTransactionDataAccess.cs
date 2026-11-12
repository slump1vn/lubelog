using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class BankTransactionDataAccess : IBankTransactionDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "bank_transactions";
        //webhook and sync can arrive at the same moment; LiteDB has no insert-if-absent.
        private static readonly object writeLock = new object();
        public BankTransactionDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        private ILiteCollection<BankTransaction> GetTable()
        {
            var table = _liteDB.GetLiteDB().GetCollection<BankTransaction>(tableName);
            table.EnsureIndex(x => x.Fingerprint, true);
            table.EnsureIndex(x => x.HouseholdId);
            return table;
        }
        public bool AddIfNew(BankTransaction transaction)
        {
            lock (writeLock)
            {
                var table = GetTable();
                if (table.Exists(x => x.Fingerprint == transaction.Fingerprint))
                {
                    return false;
                }
                transaction.Id = 0;
                table.Insert(transaction);
                _liteDB.GetLiteDB().Checkpoint();
                return true;
            }
        }
        //LiteDB stores dates in UTC but hands them back as local time.
        private static BankTransaction AsUtc(BankTransaction transaction)
        {
            transaction.OccurredAtUtc = transaction.OccurredAtUtc.ToUniversalTime();
            transaction.ImportedAtUtc = transaction.ImportedAtUtc.ToUniversalTime();
            return transaction;
        }
        public List<BankTransaction> GetRecent(int householdId, int limit)
        {
            return GetTable().Query()
                .Where(x => x.HouseholdId == householdId)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Limit(limit)
                .ToList()
                .Select(AsUtc)
                .ToList();
        }
        public List<BankTransaction> GetPage(int householdId, DateTime? beforeUtc, int beforeId, int limit)
        {
            var query = GetTable().Query().Where(x => x.HouseholdId == householdId);
            if (beforeUtc.HasValue)
            {
                var before = DateTime.SpecifyKind(beforeUtc.Value, DateTimeKind.Utc);
                query = query.Where(x => x.OccurredAtUtc < before || (x.OccurredAtUtc == before && x.Id < beforeId));
            }
            return query
                .OrderByDescending(x => x.OccurredAtUtc)
                .ToList()
                .Select(AsUtc)
                //LiteDB sorts on one field, so ties on the booking time are ordered by id here.
                .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
                .Take(limit)
                .ToList();
        }
        public bool Delete(int householdId, int id)
        {
            lock (writeLock)
            {
                var table = GetTable();
                var item = table.FindById(id);
                if (item == null || item.HouseholdId != householdId)
                {
                    return false;
                }
                var deleted = table.Delete(id);
                _liteDB.GetLiteDB().Checkpoint();
                return deleted;
            }
        }
        public List<BankTransaction> GetSince(int householdId, DateTime fromUtc)
        {
            return GetTable().Query()
                .Where(x => x.HouseholdId == householdId && x.OccurredAtUtc >= fromUtc)
                .OrderBy(x => x.OccurredAtUtc)
                .ToList()
                .Select(AsUtc)
                .ToList();
        }
    }
}
