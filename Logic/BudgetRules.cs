using CarCareTracker.Models;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// A problem found in a budget profile. Message is an English format string used as the
    /// translation key; Args fill its placeholders.
    /// </summary>
    public class BudgetIssue
    {
        public string Message { get; set; } = string.Empty;
        public object[] Args { get; set; } = Array.Empty<object>();
        public BudgetIssue(string message, params object[] args)
        {
            Message = message;
            Args = args;
        }
        public override string ToString() => string.Format(Message, Args);
    }
    /// <summary>
    /// The household's spending rules as pure functions, so they can be tested without storage.
    /// </summary>
    public static class BudgetRules
    {
        /// <summary>
        /// Checks that every income is fully allocated, every fund receives exactly its monthly
        /// amount, category limits add up to their fund, and all days and references are valid.
        /// </summary>
        public static List<BudgetIssue> Validate(BudgetProfile profile)
        {
            Normalize(profile);
            var issues = new List<BudgetIssue>();
            if (string.IsNullOrWhiteSpace(profile.Currency) || profile.Currency.Length != 3 || !profile.Currency.All(char.IsAsciiLetterUpper))
            {
                issues.Add(new BudgetIssue("Currency must be a 3-letter ISO 4217 code"));
            }
            if (profile.ApprovalThreshold <= 0)
            {
                issues.Add(new BudgetIssue("Approval threshold must be greater than 0"));
            }
            if (!IsValidDay(profile.CycleStartDay))
            {
                issues.Add(new BudgetIssue("Budget cycle start day must be between 1 and 31"));
            }
            var accountNumber = profile.SharedAccount.AccountNumber;
            if (accountNumber.Length > 0 && (accountNumber.Length < 4 || accountNumber.Length > 24 || !accountNumber.All(char.IsAsciiLetterOrDigit)))
            {
                issues.Add(new BudgetIssue("The shared account number may contain only letters and digits (4 to 24)"));
            }
            CheckKeys(profile.Funds.Select(x => (x.Key, x.Name)), "Fund", issues);
            CheckKeys(profile.Incomes.Select(x => (x.Key, x.Name)), "Income", issues);
            CheckKeys(profile.Debts.Select(x => (x.Key, x.Name)), "Debt", issues);
            var funds = profile.Funds.Where(x => !string.IsNullOrWhiteSpace(x.Key)).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First());
            var allocatedToFund = funds.Keys.ToDictionary(x => x, x => 0L);
            foreach (var income in profile.Incomes)
            {
                if (income.Amount <= 0)
                {
                    issues.Add(new BudgetIssue("Income {0} must have an amount greater than 0", income.Name));
                }
                if (!IsValidDay(income.PayDay))
                {
                    issues.Add(new BudgetIssue("Income {0} must have a payday between 1 and 31", income.Name));
                }
                foreach (var allocation in income.Allocations)
                {
                    if (allocation.Amount <= 0)
                    {
                        issues.Add(new BudgetIssue("Income {0} has an allocation that is not greater than 0", income.Name));
                    }
                    if (!funds.ContainsKey(allocation.FundKey))
                    {
                        issues.Add(new BudgetIssue("Income {0} allocates to a fund that does not exist", income.Name));
                        continue;
                    }
                    allocatedToFund[allocation.FundKey] += allocation.Amount;
                }
                var allocated = income.Allocations.Sum(x => x.Amount);
                if (allocated != income.Amount)
                {
                    issues.Add(new BudgetIssue("Income {0} receives {1} but allocates {2}", income.Name, income.Amount, allocated));
                }
            }
            foreach (var fund in profile.Funds)
            {
                if (fund.MonthlyAmount < 0 || fund.TargetAmount < 0)
                {
                    issues.Add(new BudgetIssue("Fund {0} cannot have negative amounts", fund.Name));
                }
                if (funds.TryGetValue(fund.Key, out var stored) && stored == fund && allocatedToFund[fund.Key] != fund.MonthlyAmount)
                {
                    issues.Add(new BudgetIssue("Fund {0} needs {1} per month but receives {2}", fund.Name, fund.MonthlyAmount, allocatedToFund[fund.Key]));
                }
                if (fund.Categories.Any())
                {
                    CheckKeys(fund.Categories.Select(x => (x.Key, x.Name)), "Category", issues);
                    if (fund.Categories.Any(x => x.MonthlyLimit < 0))
                    {
                        issues.Add(new BudgetIssue("Fund {0} has a category with a negative limit", fund.Name));
                    }
                    var categoryTotal = fund.Categories.Sum(x => x.MonthlyLimit);
                    if (categoryTotal != fund.MonthlyAmount)
                    {
                        issues.Add(new BudgetIssue("Categories of fund {0} add up to {1} instead of {2}", fund.Name, categoryTotal, fund.MonthlyAmount));
                    }
                }
            }
            var sharedOwners = profile.Funds.Where(x => x.Kind == FundKind.Personal && x.OwnerUserId != 0).GroupBy(x => x.OwnerUserId).Where(x => x.Count() > 1);
            foreach (var owner in sharedOwners)
            {
                issues.Add(new BudgetIssue("A person can only own one personal fund"));
            }
            foreach (var debt in profile.Debts)
            {
                if (debt.MonthlyPayment <= 0)
                {
                    issues.Add(new BudgetIssue("Debt {0} must have a monthly payment greater than 0", debt.Name));
                }
                if (!IsValidDay(debt.DueDay))
                {
                    issues.Add(new BudgetIssue("Debt {0} must have a due day between 1 and 31", debt.Name));
                }
                if (debt.RemainingBalance.HasValue && debt.RemainingBalance.Value < 0)
                {
                    issues.Add(new BudgetIssue("Debt {0} cannot have a negative balance", debt.Name));
                }
                if (debt.AnnualInterestRate.HasValue && (debt.AnnualInterestRate.Value < 0 || debt.AnnualInterestRate.Value > 100))
                {
                    issues.Add(new BudgetIssue("Debt {0} must have an interest rate between 0 and 100 percent", debt.Name));
                }
                if (!funds.TryGetValue(debt.FundKey, out var debtFund) || debtFund.Kind != FundKind.Debt)
                {
                    issues.Add(new BudgetIssue("Debt {0} must be paid from a debt fund", debt.Name));
                }
            }
            foreach (var debtFund in profile.Funds.Where(x => x.Kind == FundKind.Debt))
            {
                var payments = profile.Debts.Where(x => x.FundKey == debtFund.Key).Sum(x => x.MonthlyPayment);
                if (payments > debtFund.MonthlyAmount)
                {
                    issues.Add(new BudgetIssue("Debt payments of {0} exceed what fund {1} receives ({2})", payments, debtFund.Name, debtFund.MonthlyAmount));
                }
            }
            return issues;
        }
        /// <summary>
        /// Whether spending needs the other partner's approval before it counts against a fund.
        /// Personal funds never do; emergency withdrawals always do; other funds do when the
        /// spending is unplanned and at or above the threshold.
        /// </summary>
        public static bool RequiresApproval(FundKind fundKind, long amount, bool isPlanned, long approvalThreshold)
        {
            switch (fundKind)
            {
                case FundKind.Personal:
                    return false;
                case FundKind.Emergency:
                    return amount > 0;
                default:
                    return !isPlanned && amount >= approvalThreshold;
            }
        }
        /// <summary>
        /// The calendar date of a day-of-month setting in a given month. A day past the end of a
        /// short month falls on that month's last day (31 in February is the 28th or 29th).
        /// </summary>
        public static DateOnly ResolveDay(int year, int month, int day)
        {
            var lastDay = DateTime.DaysInMonth(year, month);
            return new DateOnly(year, month, Math.Clamp(day, 1, lastDay));
        }
        /// <summary>
        /// First day of the budget cycle that contains the date.
        /// </summary>
        public static DateOnly GetCycleStart(DateOnly date, int cycleStartDay)
        {
            var startThisMonth = ResolveDay(date.Year, date.Month, cycleStartDay);
            if (date >= startThisMonth)
            {
                return startThisMonth;
            }
            var previous = date.AddMonths(-1);
            return ResolveDay(previous.Year, previous.Month, cycleStartDay);
        }
        /// <summary>
        /// Months of saving needed to reach a target, or null when nothing is saved each month.
        /// </summary>
        public static int? MonthsToTarget(long currentBalance, long targetAmount, long monthlyAmount)
        {
            if (currentBalance >= targetAmount)
            {
                return 0;
            }
            if (monthlyAmount <= 0)
            {
                return null;
            }
            var remaining = targetAmount - currentBalance;
            return (int)((remaining + monthlyAmount - 1) / monthlyAmount);
        }
        /// <summary>
        /// The household's starting template from the agreed 25,000,000 VND plan: 9,000,000 on
        /// the 5th pays the debt first and keeps 1,000,000 for groceries; 16,000,000 on the 20th
        /// fills the shared, family, personal and emergency funds.
        /// </summary>
        public static BudgetProfile CreateFamilyTemplate(int householdId, IReadOnlyList<(int UserId, string Name)> members)
        {
            var first = members.Count > 0 ? members[0] : (0, "1");
            var second = members.Count > 1 ? members[1] : (0, "2");
            return new BudgetProfile
            {
                Id = householdId,
                Currency = "VND",
                ApprovalThreshold = 1_000_000,
                CycleStartDay = 20,
                Funds = new List<Fund>
                {
                    new Fund { Key = "debt", Name = "Trả nợ", Kind = FundKind.Debt, MonthlyAmount = 8_000_000 },
                    new Fund
                    {
                        Key = "living", Name = "Sinh hoạt chung", Kind = FundKind.Shared, MonthlyAmount = 10_000_000,
                        Categories = new List<BudgetCategory>
                        {
                            new BudgetCategory { Key = "food", Name = "Ăn uống, đi chợ", MonthlyLimit = 5_500_000 },
                            new BudgetCategory { Key = "utilities", Name = "Điện, nước, internet, phí dịch vụ", MonthlyLimit = 2_000_000 },
                            new BudgetCategory { Key = "household", Name = "Gia dụng, y tế, phát sinh nhỏ", MonthlyLimit = 1_500_000 },
                            new BudgetCategory { Key = "buffer", Name = "Dự phòng chờ lương", MonthlyLimit = 1_000_000 }
                        }
                    },
                    new Fund { Key = "family", Name = "Gắn kết gia đình", Kind = FundKind.Shared, MonthlyAmount = 1_500_000 },
                    new Fund { Key = "personal-1", Name = $"Quỹ riêng {first.Item2}", Kind = FundKind.Personal, MonthlyAmount = 1_750_000, OwnerUserId = first.Item1 },
                    new Fund { Key = "personal-2", Name = $"Quỹ riêng {second.Item2}", Kind = FundKind.Personal, MonthlyAmount = 1_750_000, OwnerUserId = second.Item1 },
                    new Fund { Key = "emergency", Name = "Quỹ khẩn cấp", Kind = FundKind.Emergency, MonthlyAmount = 2_000_000, TargetAmount = 20_000_000 }
                },
                Incomes = new List<IncomeSource>
                {
                    new IncomeSource
                    {
                        Key = "pay-5", Name = "Lương ngày 5", Amount = 9_000_000, PayDay = 5,
                        Allocations = new List<IncomeAllocation>
                        {
                            new IncomeAllocation { FundKey = "debt", Amount = 8_000_000, Order = 1 },
                            new IncomeAllocation { FundKey = "living", Amount = 1_000_000, Order = 2 }
                        }
                    },
                    new IncomeSource
                    {
                        Key = "pay-20", Name = "Lương ngày 20", Amount = 16_000_000, PayDay = 20,
                        Allocations = new List<IncomeAllocation>
                        {
                            new IncomeAllocation { FundKey = "living", Amount = 9_000_000, Order = 1 },
                            new IncomeAllocation { FundKey = "family", Amount = 1_500_000, Order = 2 },
                            new IncomeAllocation { FundKey = "personal-1", Amount = 1_750_000, Order = 3 },
                            new IncomeAllocation { FundKey = "personal-2", Amount = 1_750_000, Order = 4 },
                            new IncomeAllocation { FundKey = "emergency", Amount = 2_000_000, Order = 5 }
                        }
                    }
                },
                Debts = new List<Debt>
                {
                    new Debt { Key = "loan", Name = "Khoản nợ định kỳ", MonthlyPayment = 8_000_000, DueDay = 21, FundKey = "debt" }
                }
            };
        }
        /// <summary>
        /// Replaces missing lists and keys (for example from a hand-written JSON body) with empty
        /// values, so the checks below report them instead of failing.
        /// </summary>
        public static void Normalize(BudgetProfile profile)
        {
            profile.Currency ??= string.Empty;
            profile.SharedAccount ??= new SharedAccount();
            profile.SharedAccount.AccountNumber = (profile.SharedAccount.AccountNumber ?? string.Empty).Replace(" ", string.Empty).Trim();
            profile.SharedAccount.BankName = (profile.SharedAccount.BankName ?? string.Empty).Trim();
            profile.Funds ??= new List<Fund>();
            profile.Incomes ??= new List<IncomeSource>();
            profile.Debts ??= new List<Debt>();
            profile.Funds.RemoveAll(x => x == null);
            profile.Incomes.RemoveAll(x => x == null);
            profile.Debts.RemoveAll(x => x == null);
            foreach (var fund in profile.Funds)
            {
                fund.Key ??= string.Empty;
                fund.Name ??= string.Empty;
                fund.Categories ??= new List<BudgetCategory>();
                fund.Categories.RemoveAll(x => x == null);
                foreach (var category in fund.Categories)
                {
                    category.Key ??= string.Empty;
                    category.Name ??= string.Empty;
                }
            }
            foreach (var income in profile.Incomes)
            {
                income.Key ??= string.Empty;
                income.Name ??= string.Empty;
                income.Allocations ??= new List<IncomeAllocation>();
                income.Allocations.RemoveAll(x => x == null);
                foreach (var allocation in income.Allocations)
                {
                    allocation.FundKey ??= string.Empty;
                }
            }
            foreach (var debt in profile.Debts)
            {
                debt.Key ??= string.Empty;
                debt.Name ??= string.Empty;
                debt.FundKey ??= string.Empty;
            }
        }
        private static bool IsValidDay(int day) => day >= 1 && day <= 31;
        private static void CheckKeys(IEnumerable<(string Key, string Name)> items, string kind, List<BudgetIssue> issues)
        {
            var list = items.ToList();
            if (list.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Name)))
            {
                issues.Add(new BudgetIssue($"Every {kind.ToLowerInvariant()} needs a name"));
            }
            foreach (var duplicate in list.Where(x => !string.IsNullOrWhiteSpace(x.Key)).GroupBy(x => x.Key).Where(x => x.Count() > 1))
            {
                issues.Add(new BudgetIssue($"{kind} {{0}} appears more than once", duplicate.First().Name));
            }
        }
    }
}
