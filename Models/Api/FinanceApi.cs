namespace CarCareTracker.Models.Api
{
    // Version 1 of the JSON API for the native apps. Field names are camelCase, dates are
    // yyyy-MM-dd Vietnam calendar dates, times are UTC ISO 8601, money is whole VND.
    // Fields are only ever added to v1; a change that breaks clients goes to v2.

    public class MeDto
    {
        public int Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int HouseholdId { get; set; }
        public bool IsAdmin { get; set; }
    }
    public class AccountDto
    {
        public bool Linked { get; set; }
        public string BankName { get; set; } = string.Empty;
        /// <summary>Only the last four digits, e.g. "•••• 6789".</summary>
        public string MaskedNumber { get; set; } = string.Empty;
        /// <summary>Null until the bank has reported a balance.</summary>
        public long? Balance { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public bool? LastSyncSucceeded { get; set; }
    }
    public class CycleDto
    {
        public DateOnly Start { get; set; }
        public DateOnly End { get; set; }
        public int Day { get; set; }
        public int Days { get; set; }
        public long MoneyIn { get; set; }
        public long MoneyOut { get; set; }
        public long Net { get; set; }
        /// <summary>Money out for the whole cycle at the pace so far; null in the first two days.</summary>
        public long? ProjectedOut { get; set; }
    }
    public class BalancePointDto
    {
        public DateOnly Date { get; set; }
        public long Balance { get; set; }
    }
    public class UpcomingDto
    {
        public DateOnly Date { get; set; }
        public string Name { get; set; } = string.Empty;
        public long Amount { get; set; }
        /// <summary>"in" or "out".</summary>
        public string Direction { get; set; } = string.Empty;
    }
    public class TransactionDto
    {
        public int Id { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        /// <summary>"in" or "out".</summary>
        public string Direction { get; set; } = string.Empty;
        public long Amount { get; set; }
        public long? Balance { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }
    public class DashboardDto
    {
        public DateOnly Today { get; set; }
        public AccountDto Account { get; set; } = new();
        public CycleDto Cycle { get; set; } = new();
        public List<BalancePointDto> BalanceHistory { get; set; } = new();
        public List<UpcomingDto> Upcoming { get; set; } = new();
        public List<TransactionDto> Recent { get; set; } = new();
    }
    public class TransactionPageDto
    {
        public List<TransactionDto> Items { get; set; } = new();
        /// <summary>Pass as "cursor" to get the next (older) page; null on the last page.</summary>
        public string? NextCursor { get; set; }
    }
}
