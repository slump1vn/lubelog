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
        /// <summary>Part of MoneyIn found only from the bank balance, not reported as a transaction.</summary>
        public long UnrecordedIn { get; set; }
        /// <summary>Part of MoneyOut found only from the bank balance, not reported as a transaction.</summary>
        public long UnrecordedOut { get; set; }
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
        /// <summary>"webhook" or "api" (SePay), "statement" (imported bank statement), "manual" (entered by hand).</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>Spending category set by hand; empty when not classified.</summary>
        public string Category { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
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
    /// <summary>The household budget plan as the apps show it. The joint account number is left out.</summary>
    public class BudgetDto
    {
        /// <summary>ISO 4217, "VND". Amounts are whole units of it.</summary>
        public string Currency { get; set; } = "VND";
        /// <summary>Unplanned spending at or above this needs the other partner's approval.</summary>
        public long ApprovalThreshold { get; set; }
        /// <summary>Day of the month a budget cycle starts.</summary>
        public int CycleStartDay { get; set; }
        /// <summary>Paydays, earliest day of the month first.</summary>
        public List<BudgetIncomeDto> Incomes { get; set; } = new();
        public List<BudgetFundDto> Funds { get; set; } = new();
        /// <summary>Debts, earliest due day first.</summary>
        public List<BudgetDebtDto> Debts { get; set; } = new();
        public DateTime UpdatedAtUtc { get; set; }
    }
    public class BudgetIncomeDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Amount { get; set; }
        /// <summary>1 to 31; short months use their last day.</summary>
        public int PayDay { get; set; }
        /// <summary>Where the money goes, in the order it is moved.</summary>
        public List<BudgetAllocationDto> Allocations { get; set; } = new();
    }
    public class BudgetAllocationDto
    {
        public string FundKey { get; set; } = string.Empty;
        public long Amount { get; set; }
    }
    public class BudgetFundDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>"shared", "personal", "debt", "emergency" or "savings".</summary>
        public string Kind { get; set; } = string.Empty;
        public long MonthlyAmount { get; set; }
        /// <summary>Target balance for emergency and savings funds; null when there is none.</summary>
        public long? TargetAmount { get; set; }
        /// <summary>Owner of a personal fund; null when not a personal fund or not assigned.</summary>
        public int? OwnerUserId { get; set; }
        public List<BudgetCategoryDto> Categories { get; set; } = new();
    }
    public class BudgetCategoryDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long MonthlyLimit { get; set; }
    }
    public class BudgetDebtDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long MonthlyPayment { get; set; }
        public int DueDay { get; set; }
        /// <summary>Null when the household has not entered it.</summary>
        public long? RemainingBalance { get; set; }
        /// <summary>Percent per year; null when unknown.</summary>
        public decimal? AnnualInterestRate { get; set; }
        public string FundKey { get; set; } = string.Empty;
    }

    /// <summary>A change of the bank balance that no recorded transaction explains.</summary>
    public class BalanceGapDto
    {
        /// <summary>Stable id of the gap; send it back to explain the gap.</summary>
        public string Key { get; set; } = string.Empty;
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public long BalanceBefore { get; set; }
        public long BalanceAfter { get; set; }
        /// <summary>Unexplained amount, always positive; see direction.</summary>
        public long Amount { get; set; }
        /// <summary>"in" or "out".</summary>
        public string Direction { get; set; } = string.Empty;
        /// <summary>Entries made by hand already inside the gap's period.</summary>
        public int ManualEntries { get; set; }
    }
    public class ReconciliationDto
    {
        /// <summary>False until the joint account number is saved on the website.</summary>
        public bool HasAccount { get; set; }
        /// <summary>Newest first.</summary>
        public List<BalanceGapDto> Gaps { get; set; } = new();
        /// <summary>Entries made by hand, newest first (at most 30).</summary>
        public List<TransactionDto> ManualEntries { get; set; } = new();
        /// <summary>Categories to choose from, as "Fund: Category", ending with "Other".</summary>
        public List<string> Categories { get; set; } = new();
    }
    /// <summary>Explains (part of) a gap. Send an Idempotency-Key header so a retry cannot add it twice.</summary>
    public class ManualTransactionRequest
    {
        public string GapKey { get; set; } = string.Empty;
        /// <summary>Whole VND, at most the gap's amount.</summary>
        public long Amount { get; set; }
        /// <summary>Vietnam calendar date the money moved, yyyy-MM-dd.</summary>
        public string Date { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
    public class SyncResultDto
    {
        public bool Succeeded { get; set; }
        /// <summary>New transactions stored by this sync.</summary>
        public int Added { get; set; }
        /// <summary>Machine-readable reason when it did not succeed, e.g. "not_configured".</summary>
        public string? Error { get; set; }
        public DateTime? LastRunUtc { get; set; }
        /// <summary>True when a sync ran moments ago and its result is returned instead of asking SePay again.</summary>
        public bool Recent { get; set; }
    }
    public class SessionDto
    {
        public int Id { get; set; }
        /// <summary>"app" or "browser".</summary>
        public string Kind { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        /// <summary>The session this request was made with.</summary>
        public bool IsCurrent { get; set; }
    }
    /// <summary>What the home AI read on a photo of a receipt or a bank transfer, and the gaps it may explain.</summary>
    public class ReceiptReadingDto
    {
        /// <summary>"receipt", "transfer" or "other" (not readable / not a receipt).</summary>
        public string Kind { get; set; } = string.Empty;
        /// <summary>Shop name, or the recipient of a transfer.</summary>
        public string Vendor { get; set; } = string.Empty;
        /// <summary>yyyy-MM-dd; null when the photo shows no date.</summary>
        public DateOnly? Date { get; set; }
        /// <summary>Whole VND; 0 when not readable.</summary>
        public long Total { get; set; }
        /// <summary>Transfer content, or a short list of the items bought.</summary>
        public string Content { get; set; } = string.Empty;
        /// <summary>Transaction code of a transfer.</summary>
        public string Reference { get; set; } = string.Empty;
        /// <summary>Money-out gaps this payment may explain, best match first.</summary>
        public List<BalanceGapDto> Gaps { get; set; } = new();
        /// <summary>A bank transaction that already records this payment, when one matches.</summary>
        public TransactionDto? AlreadyRecorded { get; set; }
    }
    /// <summary>One broken rule in a problem response, under "errors".</summary>
    public class ApiIssueDto
    {
        /// <summary>Stable, e.g. "amount_exceeds_gap"; the apps translate it.</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>English text, for logs and as a fallback.</summary>
        public string Message { get; set; } = string.Empty;
        /// <summary>Values that fill the {0}, {1} ... of the message, as text.</summary>
        public List<string> Args { get; set; } = new();
    }
}
