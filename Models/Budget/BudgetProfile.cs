namespace CarCareTracker.Models
{
    /// <summary>
    /// What a fund is for. It decides which household rules apply to spending from it.
    /// </summary>
    public enum FundKind
    {
        /// <summary>Shared household spending (food, utilities, family time).</summary>
        Shared = 0,
        /// <summary>One person's free money. The app records the transfer only, never what it is spent on.</summary>
        Personal = 1,
        /// <summary>Money set aside for debt repayments.</summary>
        Debt = 2,
        /// <summary>Emergency fund with a target balance. Withdrawals always need both partners.</summary>
        Emergency = 3,
        /// <summary>Long-term savings goals.</summary>
        Savings = 4
    }
    /// <summary>
    /// A spending line inside a fund, with its own monthly limit.
    /// </summary>
    public class BudgetCategory
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Monthly limit in whole units of the profile currency (VND has no minor unit).</summary>
        public long MonthlyLimit { get; set; }
    }
    public class Fund
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public FundKind Kind { get; set; } = FundKind.Shared;
        /// <summary>Amount put into the fund every month, in whole currency units.</summary>
        public long MonthlyAmount { get; set; }
        /// <summary>Owner of a personal fund; 0 when not assigned yet.</summary>
        public int OwnerUserId { get; set; }
        /// <summary>Target balance, used by emergency and savings funds; 0 when there is none.</summary>
        public long TargetAmount { get; set; }
        public List<BudgetCategory> Categories { get; set; } = new List<BudgetCategory>();
    }
    /// <summary>
    /// Part of a payday that goes into one fund, in the order the household moves the money.
    /// </summary>
    public class IncomeAllocation
    {
        public string FundKey { get; set; } = string.Empty;
        public long Amount { get; set; }
        public int Order { get; set; }
    }
    public class IncomeSource
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Amount { get; set; }
        /// <summary>Day of the month the money arrives (1-31). Short months use their last day.</summary>
        public int PayDay { get; set; } = 1;
        public List<IncomeAllocation> Allocations { get; set; } = new List<IncomeAllocation>();
    }
    public class Debt
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long MonthlyPayment { get; set; }
        /// <summary>Day of the month the payment is due (1-31).</summary>
        public int DueDay { get; set; } = 1;
        /// <summary>Remaining balance; null when the household has not entered it.</summary>
        public long? RemainingBalance { get; set; }
        /// <summary>Annual interest rate in percent; null when unknown.</summary>
        public decimal? AnnualInterestRate { get; set; }
        /// <summary>Fund the payments are made from.</summary>
        public string FundKey { get; set; } = string.Empty;
    }
    /// <summary>
    /// The household's budget setup. One document per household; Id is the household head's user id.
    /// </summary>
    public class BudgetProfile
    {
        public int Id { get; set; }
        /// <summary>ISO 4217 code. Amounts are stored as whole units of this currency.</summary>
        public string Currency { get; set; } = "VND";
        /// <summary>Unplanned spending at or above this amount needs the other partner's approval.</summary>
        public long ApprovalThreshold { get; set; } = 1_000_000;
        /// <summary>Day of the month a budget cycle starts (the payday that funds the month).</summary>
        public int CycleStartDay { get; set; } = 1;
        public List<IncomeSource> Incomes { get; set; } = new List<IncomeSource>();
        public List<Fund> Funds { get; set; } = new List<Fund>();
        public List<Debt> Debts { get; set; } = new List<Debt>();
        public DateTime UpdatedAtUtc { get; set; }
        public int UpdatedByUserId { get; set; }
    }
    /// <summary>
    /// One change to budget data. Entries are only ever added, never edited or deleted.
    /// </summary>
    public class BudgetAuditEntry
    {
        public int Id { get; set; }
        public int HouseholdId { get; set; }
        public int UserId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Entity { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        /// <summary>JSON of the entity before the change; empty when it was created.</summary>
        public string Before { get; set; } = string.Empty;
        /// <summary>JSON of the entity after the change; empty when it was deleted.</summary>
        public string After { get; set; } = string.Empty;
    }
    public class BudgetSettingsViewModel
    {
        public BudgetProfile Profile { get; set; } = new BudgetProfile();
        public bool HasProfile { get; set; }
        /// <summary>The page shows the starting template, which is not saved until the user saves it.</summary>
        public bool IsTemplate { get; set; }
        public bool IsHouseholdHead { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<BudgetAuditEntry> RecentChanges { get; set; } = new List<BudgetAuditEntry>();
        public Dictionary<int, string> UserNames { get; set; } = new Dictionary<int, string>();
    }
}
