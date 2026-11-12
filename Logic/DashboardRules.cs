using CarCareTracker.Models;
using System.Globalization;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Figures for the home dashboard of the joint account, as pure functions so they can be tested
    /// without storage. Dates are Vietnam calendar dates; amounts are whole VND.
    /// </summary>
    public static class DashboardRules
    {
        /// <summary>Last day of the budget cycle that starts on cycleStart.</summary>
        public static DateOnly GetCycleEnd(DateOnly cycleStart, int cycleStartDay)
        {
            var next = cycleStart.AddMonths(1);
            return BudgetRules.ResolveDay(next.Year, next.Month, cycleStartDay).AddDays(-1);
        }
        /// <summary>The next date on or after today that a day-of-month setting falls on.</summary>
        public static DateOnly NextOccurrence(DateOnly today, int dayOfMonth)
        {
            var thisMonth = BudgetRules.ResolveDay(today.Year, today.Month, dayOfMonth);
            if (thisMonth >= today)
            {
                return thisMonth;
            }
            var next = today.AddMonths(1);
            return BudgetRules.ResolveDay(next.Year, next.Month, dayOfMonth);
        }
        /// <summary>
        /// Paydays and debt payments from today up to and including the last day given, soonest first.
        /// Each income and debt appears once, at its next date.
        /// </summary>
        public static List<UpcomingMoney> GetUpcoming(BudgetProfile? profile, DateOnly today, DateOnly until)
        {
            var result = new List<UpcomingMoney>();
            if (profile == null)
            {
                return result;
            }
            foreach (var income in profile.Incomes.Where(x => x.Amount > 0))
            {
                var date = NextOccurrence(today, income.PayDay);
                if (date <= until)
                {
                    result.Add(new UpcomingMoney { Date = date, Name = income.Name, Amount = income.Amount, Direction = TransferDirection.In });
                }
            }
            foreach (var debt in profile.Debts.Where(x => x.MonthlyPayment > 0))
            {
                var date = NextOccurrence(today, debt.DueDay);
                if (date <= until)
                {
                    result.Add(new UpcomingMoney { Date = date, Name = debt.Name, Amount = debt.MonthlyPayment, Direction = TransferDirection.Out });
                }
            }
            return result.OrderBy(x => x.Date).ThenBy(x => x.Direction).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        }
        /// <summary>
        /// End-of-day balance for every day from the first date to the last, worked backwards from the
        /// current balance and the transactions since the first date. Empty when the balance is unknown.
        /// </summary>
        public static List<BalancePoint> GetDailyBalances(IEnumerable<BankTransaction> transactionsSinceFrom, long? currentBalance, DateOnly from, DateOnly to)
        {
            var result = new List<BalancePoint>();
            if (!currentBalance.HasValue || to < from)
            {
                return result;
            }
            var netByDay = transactionsSinceFrom
                .GroupBy(x => DateOnly.FromDateTime(SepayRules.ToVietnamTime(x.OccurredAtUtc)))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Direction == TransferDirection.In ? x.Amount : -x.Amount));
            //money that moved after "to" is not part of any day shown.
            long balance = currentBalance.Value - netByDay.Where(x => x.Key > to).Sum(x => x.Value);
            for (var day = to; day >= from; day = day.AddDays(-1))
            {
                result.Add(new BalancePoint { Date = day, Balance = balance });
                if (netByDay.TryGetValue(day, out long net))
                {
                    balance -= net;
                }
            }
            result.Reverse();
            return result;
        }
        /// <summary>
        /// Money out for the whole cycle if spending keeps its pace so far. Null in the first two days,
        /// when the pace says little.
        /// </summary>
        public static long? ProjectCycleOut(long outSoFar, int dayOfCycle, int daysInCycle)
        {
            if (dayOfCycle < 3 || daysInCycle <= 0)
            {
                return null;
            }
            return (long)Math.Round((double)outSoFar / dayOfCycle * daysInCycle, MidpointRounding.AwayFromZero);
        }
        /// <summary>
        /// SVG polyline points ("x,y x,y ...") for a balance line in a box of the given size, with the
        /// lowest balance at the bottom and the highest at the top. A flat line sits in the middle.
        /// </summary>
        public static string SparklinePoints(IReadOnlyList<BalancePoint> points, double width, double height)
        {
            if (points.Count == 0)
            {
                return string.Empty;
            }
            long min = points.Min(x => x.Balance);
            long max = points.Max(x => x.Balance);
            double step = points.Count > 1 ? width / (points.Count - 1) : 0;
            return string.Join(" ", points.Select((point, index) =>
            {
                double x = points.Count > 1 ? index * step : width / 2;
                double y = max == min ? height / 2 : height - (double)(point.Balance - min) / (max - min) * height;
                return string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#}");
            }));
        }
    }
}
