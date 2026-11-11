using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    public interface IBudgetDataAccess
    {
        /// <summary>Returns null when the household has no budget yet.</summary>
        BudgetProfile? GetBudgetProfile(int householdId);
        bool SaveBudgetProfile(BudgetProfile profile);
        bool AddAuditEntry(BudgetAuditEntry entry);
        /// <summary>Newest first.</summary>
        List<BudgetAuditEntry> GetAuditEntries(int householdId, int limit);
    }
}
