using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    public interface IBankTransactionDataAccess
    {
        /// <summary>Stores the transaction unless one with the same fingerprint exists. True when it was added.</summary>
        bool AddIfNew(BankTransaction transaction);
        /// <summary>Newest first by booking time.</summary>
        List<BankTransaction> GetRecent(int householdId, int limit);
        /// <summary>Transactions booked at or after the time, oldest first.</summary>
        List<BankTransaction> GetSince(int householdId, DateTime fromUtc);
    }
}
