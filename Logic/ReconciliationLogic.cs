using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using System.Globalization;

namespace CarCareTracker.Logic
{
    public class ManualTransactionInput
    {
        /// <summary>The gap this entry explains (required: manual entries only explain bank movements SePay missed).</summary>
        public string GapKey { get; set; } = string.Empty;
        public long Amount { get; set; }
        /// <summary>Vietnam date "yyyy-MM-dd" the money moved; it is placed inside the gap's period.</summary>
        public string Date { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
    public class ManualTransactionDelete
    {
        public int Id { get; set; }
    }
    public class ReconciliationViewModel
    {
        public bool HasAccount { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public List<BalanceGap> Gaps { get; set; } = new List<BalanceGap>();
        public List<BankTransaction> ManualEntries { get; set; } = new List<BankTransaction>();
        public List<string> Categories { get; set; } = new List<string>();
        public int StatementTransactions { get; set; }
        public DateTime? LastStatementTransactionUtc { get; set; }
    }
    public class StatementImportPreviewRow
    {
        public int Line { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public string Direction { get; set; } = string.Empty;
        public long Amount { get; set; }
        public long? Balance { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
    }
    public class StatementImportResult
    {
        public bool Success { get; set; }
        public bool Applied { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new List<string>();
        public Dictionary<string, string> Columns { get; set; } = new Dictionary<string, string>();
        public int Rows { get; set; }
        public int New { get; set; }
        public int AlreadyRecorded { get; set; }
        public int ReplacesManual { get; set; }
        public int SkippedLines { get; set; }
        public int GapsBefore { get; set; }
        public int GapsAfter { get; set; }
        public List<StatementImportPreviewRow> Preview { get; set; } = new List<StatementImportPreviewRow>();
        /// <summary>Last four digits of the statement's account and of the shared account, when they differ.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public (string Statement, string Shared)? AccountMismatch { get; set; }
    }
    public interface IReconciliationLogic
    {
        ReconciliationViewModel GetView(int householdId);
        OperationResponse AddManual(int householdId, int userId, ManualTransactionInput input);
        OperationResponse DeleteManual(int householdId, int id);
        StatementImportResult ImportStatement(int householdId, int userId, byte[] content, string fileName, bool apply);
    }

    /// <summary>
    /// Bank reconciliation for the joint account: shows where the bank balance moved without a recorded
    /// transaction, lets the family explain those amounts by category, and imports bank statements to
    /// fill in what SePay did not report.
    /// </summary>
    public class ReconciliationLogic : IReconciliationLogic
    {
        private const string OtherCategory = "Other";
        private readonly IBankTransactionDataAccess _transactionData;
        private readonly IBudgetDataAccess _budgetData;
        private readonly ILogger<ReconciliationLogic> _logger;
        private static readonly object importLock = new object();
        public ReconciliationLogic(IBankTransactionDataAccess transactionData, IBudgetDataAccess budgetData, ILogger<ReconciliationLogic> logger)
        {
            _transactionData = transactionData;
            _budgetData = budgetData;
            _logger = logger;
        }
        private List<BankTransaction> All(int householdId) => _transactionData.GetSince(householdId, SepayLogic.HistoryStartUtc);

        public ReconciliationViewModel GetView(int householdId)
        {
            var profile = _budgetData.GetBudgetProfile(householdId);
            var all = All(householdId);
            var statement = all.Where(x => x.Source == ReconciliationRules.SourceStatement).ToList();
            return new ReconciliationViewModel
            {
                HasAccount = !string.IsNullOrEmpty(profile?.SharedAccount?.AccountNumber),
                AccountNumber = profile?.SharedAccount?.AccountNumber ?? string.Empty,
                Gaps = ReconciliationRules.FindGaps(all).OrderByDescending(x => x.ToUtc).ToList(),
                ManualEntries = all.Where(x => x.Source == ReconciliationRules.SourceManual).OrderByDescending(x => x.OccurredAtUtc).Take(30).ToList(),
                Categories = Categories(profile),
                StatementTransactions = statement.Count,
                LastStatementTransactionUtc = statement.Any() ? statement.Max(x => x.OccurredAtUtc) : null
            };
        }

        /// <summary>Spending categories from the budget ("Fund: Category"), then funds without categories, then Other.</summary>
        public static List<string> Categories(BudgetProfile? profile)
        {
            var result = new List<string>();
            foreach (var fund in profile?.Funds ?? new List<Fund>())
            {
                if (fund.Categories.Any())
                {
                    result.AddRange(fund.Categories.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => $"{fund.Name}: {x.Name}"));
                }
                else if (!string.IsNullOrWhiteSpace(fund.Name))
                {
                    result.Add(fund.Name);
                }
            }
            result.Add(OtherCategory);
            return result.Distinct().ToList();
        }

        public OperationResponse AddManual(int householdId, int userId, ManualTransactionInput input)
        {
            var all = All(householdId);
            var gap = ReconciliationRules.FindGaps(all).FirstOrDefault(x => x.Key == input.GapKey);
            if (gap == null)
            {
                return OperationResponse.Failed("This difference is already explained or no longer exists. Reload the page.");
            }
            if (!DateOnly.TryParseExact(input.Date ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return OperationResponse.Failed("Pick a date");
            }
            var occurredAtUtc = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(12, 0)) - SepayRules.VietnamOffset, DateTimeKind.Utc);
            var issues = ReconciliationRules.ValidateManual(input.Amount, occurredAtUtc, input.Category, input.Note, DateTime.UtcNow);
            if (input.Amount > gap.AbsoluteAmount)
            {
                issues.Add("The amount is more than the unexplained difference");
            }
            if (issues.Any())
            {
                return OperationResponse.Failed(string.Join("\n", issues));
            }
            //the entry must sit between the two bank lines around the gap to explain it.
            var earliest = gap.FromUtc.AddSeconds(1);
            var latest = gap.ToUtc.AddSeconds(-1);
            if (latest <= earliest)
            {
                occurredAtUtc = gap.FromUtc + TimeSpan.FromTicks((gap.ToUtc - gap.FromUtc).Ticks / 2);
            }
            else if (occurredAtUtc < earliest)
            {
                occurredAtUtc = earliest;
            }
            else if (occurredAtUtc > latest)
            {
                occurredAtUtc = latest;
            }
            var profile = _budgetData.GetBudgetProfile(householdId);
            var transaction = new BankTransaction
            {
                HouseholdId = householdId,
                AccountNumber = profile?.SharedAccount?.AccountNumber ?? string.Empty,
                Direction = gap.Direction,
                Amount = input.Amount,
                OccurredAtUtc = occurredAtUtc,
                Category = string.IsNullOrWhiteSpace(input.Category) ? OtherCategory : input.Category.Trim(),
                Note = (input.Note ?? string.Empty).Trim(),
                Content = (input.Note ?? string.Empty).Trim(),
                Source = ReconciliationRules.SourceManual,
                CreatedByUserId = userId,
                ImportedAtUtc = DateTime.UtcNow,
                Fingerprint = "manual:" + Guid.NewGuid().ToString("N")
            };
            if (!_transactionData.AddIfNew(transaction))
            {
                return OperationResponse.Failed("The entry could not be saved");
            }
            return OperationResponse.Succeed("Saved");
        }

        public OperationResponse DeleteManual(int householdId, int id)
        {
            var entry = All(householdId).FirstOrDefault(x => x.Id == id);
            if (entry == null || entry.Source != ReconciliationRules.SourceManual)
            {
                //bank lines (SePay, statements) are never deleted by hand.
                return OperationResponse.Failed("Only entries made by hand can be deleted");
            }
            return _transactionData.Delete(householdId, id) ? OperationResponse.Succeed("Deleted") : OperationResponse.Failed("The entry could not be deleted");
        }

        public StatementImportResult ImportStatement(int householdId, int userId, byte[] content, string fileName, bool apply)
        {
            var result = new StatementImportResult();
            var profile = _budgetData.GetBudgetProfile(householdId);
            var accountNumber = profile?.SharedAccount?.AccountNumber ?? string.Empty;
            if (string.IsNullOrEmpty(accountNumber))
            {
                result.Errors.Add("Enter the shared account number under General and save first.");
                return result;
            }
            var parsed = StatementParser.Parse(content, fileName);
            result.Columns = parsed.Columns;
            result.SkippedLines = parsed.SkippedLines;
            result.Errors.AddRange(parsed.Errors);
            if (!string.IsNullOrEmpty(parsed.AccountNumber) && parsed.AccountNumber.Length >= 6 && parsed.AccountNumber != accountNumber)
            {
                result.AccountMismatch = (parsed.AccountNumber[^4..], accountNumber.Length > 4 ? accountNumber[^4..] : accountNumber);
                result.Errors.Add("This statement is for account •••• {0}, not the shared account •••• {1}.");
            }
            if (result.Errors.Any())
            {
                return result;
            }
            lock (importLock)
            {
                var all = All(householdId);
                result.GapsBefore = ReconciliationRules.FindGaps(all).Count;
                var plans = StatementImportRules.Plan(parsed.Rows, all, householdId, accountNumber);
                result.Rows = plans.Count;
                result.New = plans.Count(x => x.Outcome == StatementRowOutcome.New);
                result.AlreadyRecorded = plans.Count(x => x.Outcome == StatementRowOutcome.AlreadyRecorded);
                result.ReplacesManual = plans.Count(x => x.Outcome == StatementRowOutcome.ReplacesManual);
                result.Preview = plans.OrderByDescending(x => x.Row.OccurredAtUtc).Take(100).Select(x => new StatementImportPreviewRow
                {
                    Line = x.Row.Line,
                    OccurredAtUtc = x.Row.OccurredAtUtc,
                    Direction = x.Row.Direction == TransferDirection.In ? "in" : "out",
                    Amount = x.Row.Amount,
                    Balance = x.Row.Balance,
                    Description = x.Row.Description.Length > 120 ? x.Row.Description[..120] + "…" : x.Row.Description,
                    Outcome = x.Outcome.ToString()
                }).ToList();
                if (apply)
                {
                    var now = DateTime.UtcNow;
                    foreach (var plan in plans.Where(x => x.Outcome != StatementRowOutcome.AlreadyRecorded))
                    {
                        var transaction = StatementImportRules.ToTransaction(plan.Row, householdId, accountNumber, userId, now);
                        if (plan.Outcome == StatementRowOutcome.ReplacesManual && plan.Match != null)
                        {
                            transaction.Category = plan.Match.Category;
                            transaction.Note = plan.Match.Note;
                        }
                        if (_transactionData.AddIfNew(transaction) && plan.Outcome == StatementRowOutcome.ReplacesManual && plan.Match != null)
                        {
                            _transactionData.Delete(householdId, plan.Match.Id);
                        }
                    }
                    result.Applied = true;
                    result.GapsAfter = ReconciliationRules.FindGaps(All(householdId)).Count;
                    _logger.LogInformation("Statement imported for household {HouseholdId}: {New} new, {Replaced} replaced manual entries, {Known} already recorded.", householdId, result.New, result.ReplacesManual, result.AlreadyRecorded);
                }
            }
            result.Success = true;
            return result;
        }
    }
}
