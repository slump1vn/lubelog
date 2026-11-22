using CarCareTracker.Models;

namespace CarCareTracker.Logic
{
    public enum StatementRowOutcome
    {
        /// <summary>Not in the app yet: will be added.</summary>
        New,
        /// <summary>Already recorded (SePay or an earlier import): skipped.</summary>
        AlreadyRecorded,
        /// <summary>Matches a manual entry: the bank line replaces it and keeps its category and note.</summary>
        ReplacesManual
    }
    public class StatementRowPlan
    {
        public StatementRow Row { get; set; } = new StatementRow();
        public StatementRowOutcome Outcome { get; set; }
        /// <summary>The stored transaction it matched, when there is one.</summary>
        public BankTransaction? Match { get; set; }
    }

    /// <summary>
    /// Decides what an imported statement adds, and keeps SePay, imported statements and manual
    /// entries from counting the same bank transaction twice.
    /// </summary>
    public static class StatementImportRules
    {
        /// <summary>
        /// The stored transaction that is the same bank movement as the candidate, or null. Same direction
        /// and amount, booked within a day (two for manual entries, which carry only the date the user
        /// picked); when both carry a bank balance the balances must agree, which tells apart two equal
        /// payments on the same day. The closest balance, then the closest time, wins.
        /// </summary>
        public static BankTransaction? FindCounterpart(BankTransaction candidate, IEnumerable<BankTransaction> existing, ISet<int>? alreadyUsed = null)
        {
            BankTransaction? best = null;
            int bestScore = int.MaxValue;
            double bestDistance = double.MaxValue;
            foreach (var item in existing)
            {
                if (item.Direction != candidate.Direction || item.Amount != candidate.Amount || item.Source == ReconciliationRules.SourceGap)
                {
                    continue;
                }
                if (alreadyUsed != null && item.Id != 0 && alreadyUsed.Contains(item.Id))
                {
                    continue;
                }
                bool manual = item.Source == ReconciliationRules.SourceManual || candidate.Source == ReconciliationRules.SourceManual;
                var distance = Math.Abs((item.OccurredAtUtc - candidate.OccurredAtUtc).TotalHours);
                if (distance > (manual ? 48 : 30))
                {
                    continue;
                }
                int score;
                if (item.Balance.HasValue && candidate.Balance.HasValue)
                {
                    if (item.Balance.Value != candidate.Balance.Value)
                    {
                        continue;
                    }
                    score = 0;
                }
                else
                {
                    score = 1;
                }
                if (score < bestScore || (score == bestScore && distance < bestDistance))
                {
                    best = item;
                    bestScore = score;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static BankTransaction ToTransaction(StatementRow row, int householdId, string accountNumber, int userId, DateTime nowUtc)
        {
            var transaction = new BankTransaction
            {
                HouseholdId = householdId,
                AccountNumber = accountNumber,
                Direction = row.Direction,
                Amount = row.Amount,
                Balance = row.Balance,
                OccurredAtUtc = row.OccurredAtUtc,
                Content = row.Description,
                Reference = row.Reference,
                Source = ReconciliationRules.SourceStatement,
                CreatedByUserId = userId,
                ImportedAtUtc = nowUtc
            };
            transaction.Fingerprint = "statement:" + SepayRules.Fingerprint(householdId, accountNumber, row.Direction, row.Amount, row.OccurredAtUtc, row.Reference, row.Description + "|" + row.Balance);
            return transaction;
        }

        /// <summary>What each statement line will do. Every stored transaction is matched at most once.</summary>
        public static List<StatementRowPlan> Plan(IEnumerable<StatementRow> rows, IEnumerable<BankTransaction> existing, int householdId, string accountNumber)
        {
            var stored = existing.Where(x => x.Source != ReconciliationRules.SourceGap).ToList();
            var used = new HashSet<int>();
            var plans = new List<StatementRowPlan>();
            foreach (var row in rows.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Line))
            {
                var candidate = ToTransaction(row, householdId, accountNumber, 0, DateTime.UtcNow);
                var match = FindCounterpart(candidate, stored, used);
                var plan = new StatementRowPlan { Row = row, Match = match };
                if (match == null)
                {
                    plan.Outcome = StatementRowOutcome.New;
                }
                else
                {
                    used.Add(match.Id);
                    plan.Outcome = match.Source == ReconciliationRules.SourceManual ? StatementRowOutcome.ReplacesManual : StatementRowOutcome.AlreadyRecorded;
                }
                plans.Add(plan);
            }
            return plans;
        }
    }
}
