using CarCareTracker.Models;
using System.Security.Cryptography;
using System.Text;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// A difference between the balance the bank reports and the balance the recorded transactions
    /// explain. It is money that moved without being recorded (for example card payments SePay does not
    /// see on a free plan), somewhere between two transactions that carry a bank balance.
    /// </summary>
    public class BalanceGap
    {
        /// <summary>Stable key of the gap: the two bank transactions around it.</summary>
        public string Key { get; set; } = string.Empty;
        /// <summary>Booking time of the transaction before the gap (the last known balance).</summary>
        public DateTime FromUtc { get; set; }
        /// <summary>Booking time of the transaction that revealed the gap.</summary>
        public DateTime ToUtc { get; set; }
        public long BalanceBefore { get; set; }
        public long BalanceAfter { get; set; }
        /// <summary>Money moved without a record: negative for money out, positive for money in.</summary>
        public long Amount { get; set; }
        public TransferDirection Direction => Amount < 0 ? TransferDirection.Out : TransferDirection.In;
        public long AbsoluteAmount => Math.Abs(Amount);
        /// <summary>Manual entries already booked inside the gap's period (they are part of the explanation).</summary>
        public int ManualEntries { get; set; }
    }

    /// <summary>
    /// Bank reconciliation, as accountants do it: the bank balance is the truth, and the recorded
    /// transactions must explain every change of it. Pure functions so they can be tested without storage.
    /// </summary>
    public static class ReconciliationRules
    {
        public const string SourceManual = "manual";
        public const string SourceStatement = "statement";
        public const string SourceGap = "gap";

        public static long Signed(BankTransaction transaction) => transaction.Direction == TransferDirection.In ? transaction.Amount : -transaction.Amount;

        /// <summary>
        /// Oldest first. Transactions booked in the same second are put in the order their balances
        /// chain, because the bank time alone cannot tell which came first.
        /// </summary>
        public static List<BankTransaction> Order(IEnumerable<BankTransaction> transactions)
        {
            var result = new List<BankTransaction>();
            foreach (var group in transactions.GroupBy(x => x.OccurredAtUtc).OrderBy(x => x.Key))
            {
                var items = group.OrderBy(x => x.Id).ToList();
                if (items.Count > 1 && items.All(x => x.Balance.HasValue))
                {
                    //start from the one whose balance before it is no other item's balance after.
                    var chained = new List<BankTransaction>();
                    var remaining = new List<BankTransaction>(items);
                    var previousBalance = result.LastOrDefault(x => x.Balance.HasValue)?.Balance;
                    while (remaining.Any())
                    {
                        var next = previousBalance.HasValue ? remaining.FirstOrDefault(x => x.Balance!.Value - Signed(x) == previousBalance.Value) : null;
                        next ??= remaining.FirstOrDefault(x => !remaining.Any(y => y != x && y.Balance!.Value == x.Balance!.Value - Signed(x))) ?? remaining[0];
                        chained.Add(next);
                        remaining.Remove(next);
                        previousBalance = next.Balance;
                    }
                    items = chained;
                }
                result.AddRange(items);
            }
            return result;
        }

        /// <summary>
        /// Every gap between two transactions that carry a bank balance. Transactions without a
        /// balance in between (manual entries) count towards explaining the change.
        /// </summary>
        public static List<BalanceGap> FindGaps(IEnumerable<BankTransaction> transactions)
        {
            var gaps = new List<BalanceGap>();
            BankTransaction? lastWithBalance = null;
            long movedSince = 0;
            int manualSince = 0;
            foreach (var transaction in Order(transactions))
            {
                if (!transaction.Balance.HasValue)
                {
                    if (lastWithBalance != null)
                    {
                        movedSince += Signed(transaction);
                        if (transaction.Source == SourceManual)
                        {
                            manualSince++;
                        }
                    }
                    continue;
                }
                if (lastWithBalance != null)
                {
                    var expected = lastWithBalance.Balance!.Value + movedSince + Signed(transaction);
                    var gap = transaction.Balance.Value - expected;
                    if (gap != 0)
                    {
                        gaps.Add(new BalanceGap
                        {
                            Key = GapKey(lastWithBalance, transaction),
                            FromUtc = lastWithBalance.OccurredAtUtc,
                            ToUtc = transaction.OccurredAtUtc,
                            BalanceBefore = lastWithBalance.Balance.Value,
                            BalanceAfter = transaction.Balance.Value,
                            Amount = gap,
                            ManualEntries = manualSince
                        });
                    }
                }
                lastWithBalance = transaction;
                movedSince = 0;
                manualSince = 0;
            }
            return gaps;
        }

        /// <summary>
        /// The transactions plus one stand-in entry per gap, booked at the moment the gap was found, so
        /// totals and the daily balance line add up to what the bank reports.
        /// </summary>
        public static List<BankTransaction> WithGaps(IEnumerable<BankTransaction> transactions)
        {
            var list = transactions.ToList();
            var householdId = list.FirstOrDefault()?.HouseholdId ?? 0;
            foreach (var gap in FindGaps(list))
            {
                list.Add(new BankTransaction
                {
                    HouseholdId = householdId,
                    Fingerprint = "gap:" + gap.Key,
                    Direction = gap.Direction,
                    Amount = gap.AbsoluteAmount,
                    OccurredAtUtc = gap.ToUtc,
                    Source = SourceGap
                });
            }
            return list;
        }

        /// <summary>Money in and out found only from the balance, for gaps found on or after the date (Vietnam time).</summary>
        public static (long In, long Out) UnrecordedTotals(IEnumerable<BalanceGap> gaps, DateOnly fromVietnamDate)
        {
            var fromUtc = DateTime.SpecifyKind(fromVietnamDate.ToDateTime(TimeOnly.MinValue) - SepayRules.VietnamOffset, DateTimeKind.Utc);
            long totalIn = 0, totalOut = 0;
            foreach (var gap in gaps.Where(x => x.ToUtc >= fromUtc))
            {
                if (gap.Amount > 0)
                {
                    totalIn += gap.Amount;
                }
                else
                {
                    totalOut -= gap.Amount;
                }
            }
            return (totalIn, totalOut);
        }

        private static string GapKey(BankTransaction before, BankTransaction after)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(before.Fingerprint + ">" + after.Fingerprint));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        /// <summary>Checks a manual entry before it is stored. Returns the problems, empty when fine.</summary>
        public static List<string> ValidateManual(long amount, DateTime occurredAtUtc, string? category, string? note, DateTime nowUtc)
        {
            var issues = new List<string>();
            if (amount <= 0 || amount > 100_000_000_000)
            {
                issues.Add("The amount must be greater than 0");
            }
            if (occurredAtUtc > nowUtc.AddDays(1) || occurredAtUtc < nowUtc.AddYears(-5))
            {
                issues.Add("The date must be within the last five years");
            }
            if ((category ?? string.Empty).Length > 100)
            {
                issues.Add("The category is too long");
            }
            if ((note ?? string.Empty).Length > 300)
            {
                issues.Add("The note is too long");
            }
            return issues;
        }
    }
}
