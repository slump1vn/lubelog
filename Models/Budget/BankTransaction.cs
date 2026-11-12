using System.Text.Json.Serialization;

namespace CarCareTracker.Models
{
    /// <summary>
    /// The household's joint bank account, watched through SePay. Only the account number is
    /// stored here; the SePay keys live in the server configuration, never in the database.
    /// </summary>
    public class SharedAccount
    {
        /// <summary>Account number as the bank prints it, digits only. Empty when not set up.</summary>
        public string AccountNumber { get; set; } = string.Empty;
        /// <summary>Bank name for display, e.g. "Vietcombank".</summary>
        public string BankName { get; set; } = string.Empty;
    }
    public enum TransferDirection
    {
        In = 0,
        Out = 1
    }
    /// <summary>
    /// One movement on the shared account, as reported by the bank through SePay. Entries are
    /// only ever added; the same bank transaction reported twice (webhook and API) is stored once.
    /// </summary>
    public class BankTransaction
    {
        public int Id { get; set; }
        public int HouseholdId { get; set; }
        /// <summary>Stable key built from the bank's data, used to drop duplicates.</summary>
        public string Fingerprint { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string Bank { get; set; } = string.Empty;
        public TransferDirection Direction { get; set; }
        /// <summary>Always positive, in whole VND.</summary>
        public long Amount { get; set; }
        /// <summary>Account balance after the transaction; null when the bank does not report it.</summary>
        public long? Balance { get; set; }
        /// <summary>When the bank booked it, in UTC (SePay reports Vietnam time, UTC+7).</summary>
        public DateTime OccurredAtUtc { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        /// <summary>SePay's webhook id (a number), when the transaction arrived by webhook.</summary>
        public long? SepayWebhookId { get; set; }
        /// <summary>SePay's API id (a UUID), when the transaction arrived by API sync.</summary>
        public string SepayApiId { get; set; } = string.Empty;
        /// <summary>"webhook", "api", "statement" (imported bank statement) or "manual" (entered by hand): how the transaction first reached the app.</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>Spending category key or name, set by hand; empty when not classified.</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>Free note, set by hand.</summary>
        public string Note { get; set; } = string.Empty;
        /// <summary>Who entered a manual transaction or imported a statement row; 0 for bank data.</summary>
        public int CreatedByUserId { get; set; }
        public DateTime ImportedAtUtc { get; set; }
    }
    /// <summary>Body SePay posts to the webhook URL.</summary>
    public class SepayWebhookPayload
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("gateway")] public string? Gateway { get; set; }
        /// <summary>"yyyy-MM-dd HH:mm:ss", Vietnam time.</summary>
        [JsonPropertyName("transactionDate")] public string? TransactionDate { get; set; }
        [JsonPropertyName("accountNumber")] public string? AccountNumber { get; set; }
        [JsonPropertyName("subAccount")] public string? SubAccount { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        /// <summary>"in" or "out".</summary>
        [JsonPropertyName("transferType")] public string? TransferType { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("transferAmount")] public long TransferAmount { get; set; }
        [JsonPropertyName("accumulated")] public long Accumulated { get; set; }
        [JsonPropertyName("referenceCode")] public string? ReferenceCode { get; set; }
    }
    /// <summary>One item of GET /v2/transactions.</summary>
    public class SepayApiTransaction
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("transaction_date")] public string? TransactionDate { get; set; }
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("transfer_type")] public string? TransferType { get; set; }
        [JsonPropertyName("amount_in")] public long AmountIn { get; set; }
        [JsonPropertyName("amount_out")] public long AmountOut { get; set; }
        [JsonPropertyName("accumulated")] public long Accumulated { get; set; }
        [JsonPropertyName("transaction_content")] public string? TransactionContent { get; set; }
        [JsonPropertyName("reference_number")] public string? ReferenceNumber { get; set; }
        [JsonPropertyName("bank_brand_name")] public string? BankBrandName { get; set; }
        [JsonPropertyName("bank_account_id")] public string? BankAccountId { get; set; }
    }
    /// <summary>One item of GET /v2/bank-accounts.</summary>
    public class SepayApiBankAccount
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("bank_short_name")] public string? BankShortName { get; set; }
        [JsonPropertyName("accumulated")] public long Accumulated { get; set; }
    }
    public class SepayPagination
    {
        [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
        [JsonPropertyName("last_page")] public int LastPage { get; set; }
        [JsonPropertyName("has_more")] public bool HasMore { get; set; }
    }
    public class SepayMeta
    {
        [JsonPropertyName("pagination")] public SepayPagination? Pagination { get; set; }
    }
    public class SepayListResponse<T>
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("data")] public List<T>? Data { get; set; }
        [JsonPropertyName("meta")] public SepayMeta? Meta { get; set; }
    }
    /// <summary>Outcome of the last sync for a household, kept in memory for the page.</summary>
    public class AccountSyncStatus
    {
        public DateTime? LastRunUtc { get; set; }
        public bool Succeeded { get; set; }
        public int Added { get; set; }
        public string Error { get; set; } = string.Empty;
    }
    public class SharedAccountViewModel
    {
        public SharedAccount Account { get; set; } = new SharedAccount();
        public bool ApiConfigured { get; set; }
        public bool WebhookConfigured { get; set; }
        public int SyncMinutes { get; set; }
        public AccountSyncStatus? LastSync { get; set; }
        public long? Balance { get; set; }
        public DateOnly CycleStart { get; set; }
        public long CycleIn { get; set; }
        public long CycleOut { get; set; }
        /// <summary>Part of CycleIn found only from the bank balance (not reported as a transaction).</summary>
        public long CycleUnrecordedIn { get; set; }
        /// <summary>Part of CycleOut found only from the bank balance (not reported as a transaction).</summary>
        public long CycleUnrecordedOut { get; set; }
        public List<BankTransaction> Recent { get; set; } = new List<BankTransaction>();
    }
    public class BalancePoint
    {
        public DateOnly Date { get; set; }
        public long Balance { get; set; }
    }
    /// <summary>A payday or debt payment that is coming up, from the budget profile.</summary>
    public class UpcomingMoney
    {
        public DateOnly Date { get; set; }
        public string Name { get; set; } = string.Empty;
        public long Amount { get; set; }
        public TransferDirection Direction { get; set; }
    }
    /// <summary>The home dashboard: how the joint account is doing in the current budget cycle.</summary>
    public class FinanceDashboardViewModel
    {
        public SharedAccountViewModel Account { get; set; } = new SharedAccountViewModel();
        public bool HasProfile { get; set; }
        public DateOnly Today { get; set; }
        public DateOnly CycleEnd { get; set; }
        public int DayOfCycle { get; set; }
        public int DaysInCycle { get; set; }
        public long Net => Account.CycleIn - Account.CycleOut;
        public long? ProjectedOut { get; set; }
        public List<BalancePoint> BalanceHistory { get; set; } = new List<BalancePoint>();
        public List<UpcomingMoney> Upcoming { get; set; } = new List<UpcomingMoney>();
    }
}
