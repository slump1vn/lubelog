using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class BudgetDataAccess : IBudgetDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string profileTable = "budget_profiles";
        private static string auditTable = "budget_audit";
        public BudgetDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public BudgetProfile? GetBudgetProfile(int householdId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<BudgetProfile>(profileTable);
            return table.FindById(householdId);
        }
        public bool SaveBudgetProfile(BudgetProfile profile)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<BudgetProfile>(profileTable);
            table.Upsert(profile.Id, profile);
            db.Checkpoint();
            return true;
        }
        public bool AddAuditEntry(BudgetAuditEntry entry)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<BudgetAuditEntry>(auditTable);
            entry.Id = 0;
            table.Insert(entry);
            table.EnsureIndex(x => x.HouseholdId);
            db.Checkpoint();
            return true;
        }
        public List<BudgetAuditEntry> GetAuditEntries(int householdId, int limit)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<BudgetAuditEntry>(auditTable);
            return table.Query()
                .Where(x => x.HouseholdId == householdId)
                .OrderByDescending(x => x.Id)
                .Limit(limit)
                .ToList();
        }
    }
}
