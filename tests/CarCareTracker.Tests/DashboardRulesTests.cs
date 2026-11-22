using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class DashboardRulesTests
    {
        private static BankTransaction At(int day, int hourVietnam, TransferDirection direction, long amount) => new BankTransaction
        {
            Direction = direction,
            Amount = amount,
            OccurredAtUtc = new DateTime(2026, 11, day, hourVietnam, 0, 0, DateTimeKind.Utc) - SepayRules.VietnamOffset
        };

        [Fact]
        public void CycleRunsFromTheFifthToTheFourth()
        {
            Assert.Equal(new DateOnly(2026, 12, 4), DashboardRules.GetCycleEnd(new DateOnly(2026, 11, 5), 5));
            //a cycle that starts on the 31st ends the day before the last day of a short month.
            Assert.Equal(new DateOnly(2027, 2, 27), DashboardRules.GetCycleEnd(new DateOnly(2027, 1, 31), 31));
        }

        [Fact]
        public void NextOccurrenceIsTodayOrLater()
        {
            var today = new DateOnly(2026, 11, 12);
            Assert.Equal(new DateOnly(2026, 11, 20), DashboardRules.NextOccurrence(today, 20));
            Assert.Equal(new DateOnly(2026, 11, 12), DashboardRules.NextOccurrence(today, 12));
            Assert.Equal(new DateOnly(2026, 12, 5), DashboardRules.NextOccurrence(today, 5));
        }

        [Fact]
        public void UpcomingListsPaydaysAndDebtsSoonestFirst()
        {
            var profile = new BudgetProfile
            {
                Incomes = { new IncomeSource { Name = "Lương 20", Amount = 16_000_000, PayDay = 20 }, new IncomeSource { Name = "Lương 5", Amount = 9_000_000, PayDay = 5 } },
                Debts = { new Debt { Name = "Trả nợ", MonthlyPayment = 8_000_000, DueDay = 21 }, new Debt { Name = "Chưa nhập", MonthlyPayment = 0, DueDay = 1 } }
            };
            var upcoming = DashboardRules.GetUpcoming(profile, new DateOnly(2026, 11, 12), new DateOnly(2026, 12, 11));
            Assert.Equal(new[] { "Lương 20", "Trả nợ", "Lương 5" }, upcoming.Select(x => x.Name));
            Assert.Equal(TransferDirection.Out, upcoming[1].Direction);
            Assert.Equal(new DateOnly(2026, 12, 5), upcoming[2].Date);
            Assert.Empty(DashboardRules.GetUpcoming(profile, new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 19)));
            Assert.Empty(DashboardRules.GetUpcoming(null, new DateOnly(2026, 11, 12), new DateOnly(2026, 12, 11)));
        }

        [Fact]
        public void DailyBalancesWorkBackwardsFromTheCurrentBalance()
        {
            var transactions = new[]
            {
                At(6, 9, TransferDirection.In, 1_000_000),
                At(6, 23, TransferDirection.Out, 200_000), //23:00 in Vietnam is still the 6th
                At(8, 10, TransferDirection.Out, 300_000)
            };
            var points = DashboardRules.GetDailyBalances(transactions, 5_500_000, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 8));
            Assert.Equal(new long[] { 5_000_000, 5_800_000, 5_800_000, 5_500_000 }, points.Select(x => x.Balance));
            Assert.Equal(new DateOnly(2026, 11, 5), points[0].Date);
            Assert.Empty(DashboardRules.GetDailyBalances(transactions, null, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 8)));
        }

        [Fact]
        public void ProjectionNeedsAFewDaysOfData()
        {
            Assert.Null(DashboardRules.ProjectCycleOut(1_000_000, 2, 30));
            Assert.Equal(10_000_000, DashboardRules.ProjectCycleOut(1_000_000, 3, 30));
        }

        [Fact]
        public void SparklinePutsTheLowestBalanceAtTheBottom()
        {
            var points = new List<BalancePoint> { new() { Balance = 100 }, new() { Balance = 300 }, new() { Balance = 200 } };
            Assert.Equal("0,80 50,0 100,40", DashboardRules.SparklinePoints(points, 100, 80));
            Assert.Equal("50,40", DashboardRules.SparklinePoints(new List<BalancePoint> { new() { Balance = 5 } }, 100, 80));
            Assert.Equal(string.Empty, DashboardRules.SparklinePoints(new List<BalancePoint>(), 100, 80));
        }
    }
}
