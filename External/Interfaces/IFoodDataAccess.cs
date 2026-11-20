using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    /// <summary>Food label scans (shared by the household) and allergy profiles (one per user, private).</summary>
    public interface IFoodDataAccess
    {
        /// <summary>Stores a new scan and returns its id; 0 on failure.</summary>
        int AddScan(FoodScan scan);
        bool UpdateScan(FoodScan scan);
        FoodScan? GetScan(int householdId, int id);
        /// <summary>Newest first.</summary>
        List<FoodScan> GetScans(int householdId, int limit);
        bool DeleteScan(int householdId, int id);
        FoodProfile? GetProfile(int userId);
        bool SaveProfile(FoodProfile profile);
        bool DeleteProfile(int userId);
    }
}
