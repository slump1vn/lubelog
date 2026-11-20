using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class FoodDataAccess : IFoodDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string scanTable = "food_scans";
        private static string profileTable = "food_profiles";
        public FoodDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        private ILiteCollection<FoodScan> Scans()
        {
            var table = _liteDB.GetLiteDB().GetCollection<FoodScan>(scanTable);
            table.EnsureIndex(x => x.HouseholdId);
            return table;
        }
        //LiteDB stores dates in UTC but hands them back as local time.
        private static FoodScan AsUtc(FoodScan scan)
        {
            scan.CreatedAtUtc = scan.CreatedAtUtc.ToUniversalTime();
            return scan;
        }
        public int AddScan(FoodScan scan)
        {
            scan.Id = 0;
            Scans().Insert(scan);
            _liteDB.GetLiteDB().Checkpoint();
            return scan.Id;
        }
        public bool UpdateScan(FoodScan scan)
        {
            var updated = Scans().Update(scan);
            _liteDB.GetLiteDB().Checkpoint();
            return updated;
        }
        public FoodScan? GetScan(int householdId, int id)
        {
            var scan = Scans().FindById(id);
            return scan != null && scan.HouseholdId == householdId ? AsUtc(scan) : null;
        }
        public List<FoodScan> GetScans(int householdId, int limit)
        {
            return Scans().Query()
                .Where(x => x.HouseholdId == householdId)
                .OrderByDescending(x => x.Id)
                .Limit(limit)
                .ToList()
                .Select(AsUtc)
                .ToList();
        }
        public bool DeleteScan(int householdId, int id)
        {
            var table = Scans();
            var scan = table.FindById(id);
            if (scan == null || scan.HouseholdId != householdId)
            {
                return false;
            }
            var deleted = table.Delete(id);
            _liteDB.GetLiteDB().Checkpoint();
            return deleted;
        }
        public FoodProfile? GetProfile(int userId)
        {
            return _liteDB.GetLiteDB().GetCollection<FoodProfile>(profileTable).FindById(userId);
        }
        public bool SaveProfile(FoodProfile profile)
        {
            _liteDB.GetLiteDB().GetCollection<FoodProfile>(profileTable).Upsert(profile.Id, profile);
            _liteDB.GetLiteDB().Checkpoint();
            return true;
        }
        public bool DeleteProfile(int userId)
        {
            var deleted = _liteDB.GetLiteDB().GetCollection<FoodProfile>(profileTable).Delete(userId);
            _liteDB.GetLiteDB().Checkpoint();
            return deleted;
        }
    }
}
