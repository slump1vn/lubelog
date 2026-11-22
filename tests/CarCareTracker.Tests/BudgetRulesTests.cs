using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class BudgetRulesTests
    {
        private static BudgetProfile Template() =>
            BudgetRules.CreateFamilyTemplate(1, new List<(int, string)> { (1, "Sói"), (2, "Thỏ") });

        [Fact]
        public void FamilyTemplateIsValidAndAllocatesAll25Million()
        {
            var profile = Template();
            Assert.Empty(BudgetRules.Validate(profile));
            Assert.Equal(25_000_000, profile.Incomes.Sum(x => x.Amount));
            Assert.Equal(25_000_000, profile.Funds.Sum(x => x.MonthlyAmount));
        }

        [Fact]
        public void FamilyTemplatePaysDebtFirstOnTheFifth()
        {
            var payday = Template().Incomes.Single(x => x.PayDay == 5);
            Assert.Equal(9_000_000, payday.Amount);
            var first = payday.Allocations.OrderBy(x => x.Order).First();
            Assert.Equal("debt", first.FundKey);
            Assert.Equal(8_000_000, first.Amount);
        }

        [Fact]
        public void FamilyTemplateGivesEachPartnerTheirOwnPersonalFund()
        {
            var personal = Template().Funds.Where(x => x.Kind == FundKind.Personal).ToList();
            Assert.Equal(2, personal.Count);
            Assert.All(personal, x => Assert.Equal(1_750_000, x.MonthlyAmount));
            Assert.Equal(new[] { 1, 2 }, personal.Select(x => x.OwnerUserId).OrderBy(x => x));
        }

        [Fact]
        public void IncomeThatIsNotFullyAllocatedIsReported()
        {
            var profile = Template();
            profile.Incomes.Single(x => x.PayDay == 20).Amount = 17_000_000;
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Income {0} receives {1} but allocates {2}");
        }

        [Fact]
        public void FundThatReceivesTheWrongAmountIsReported()
        {
            var profile = Template();
            profile.Funds.Single(x => x.Key == "family").MonthlyAmount = 2_000_000;
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Fund {0} needs {1} per month but receives {2}");
        }

        [Fact]
        public void CategoriesThatDoNotAddUpToTheirFundAreReported()
        {
            var profile = Template();
            profile.Funds.Single(x => x.Key == "living").Categories[0].MonthlyLimit = 6_000_000;
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Categories of fund {0} add up to {1} instead of {2}");
        }

        [Fact]
        public void AllocationToAMissingFundIsReported()
        {
            var profile = Template();
            profile.Incomes[0].Allocations[0].FundKey = "missing";
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Income {0} allocates to a fund that does not exist");
        }

        [Fact]
        public void DebtMustBePaidFromADebtFund()
        {
            var profile = Template();
            profile.Debts[0].FundKey = "living";
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Debt {0} must be paid from a debt fund");
        }

        [Fact]
        public void APersonCannotOwnTwoPersonalFunds()
        {
            var profile = Template();
            profile.Funds.Single(x => x.Key == "personal-2").OwnerUserId = 1;
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "A person can only own one personal fund");
        }

        [Fact]
        public void DuplicateFundKeysAreReported()
        {
            var profile = Template();
            profile.Funds.Add(new Fund { Key = "family", Name = "Copy", MonthlyAmount = 0 });
            var issues = BudgetRules.Validate(profile);
            Assert.Contains(issues, x => x.Message == "Fund {0} appears more than once");
        }

        [Theory]
        [InlineData(FundKind.Personal, 5_000_000, false, false)]
        [InlineData(FundKind.Emergency, 100_000, true, true)]
        [InlineData(FundKind.Shared, 5_000_000, true, false)]
        [InlineData(FundKind.Shared, 999_999, false, false)]
        [InlineData(FundKind.Shared, 1_000_000, false, true)]
        [InlineData(FundKind.Savings, 2_500_000, false, true)]
        public void ApprovalFollowsTheHouseholdRules(FundKind kind, long amount, bool planned, bool expected)
        {
            Assert.Equal(expected, BudgetRules.RequiresApproval(kind, amount, planned, 1_000_000));
        }

        [Theory]
        [InlineData(2026, 2, 31, 28)]
        [InlineData(2028, 2, 31, 29)]
        [InlineData(2026, 4, 31, 30)]
        [InlineData(2026, 11, 20, 20)]
        public void PaydayPastTheEndOfAShortMonthFallsOnItsLastDay(int year, int month, int day, int expectedDay)
        {
            Assert.Equal(new DateOnly(year, month, expectedDay), BudgetRules.ResolveDay(year, month, day));
        }

        [Theory]
        [InlineData("2026-11-11", 20, "2026-10-20")]
        [InlineData("2026-11-20", 20, "2026-11-20")]
        [InlineData("2026-11-30", 20, "2026-11-20")]
        [InlineData("2026-01-05", 20, "2025-12-20")]
        [InlineData("2026-03-05", 31, "2026-02-28")]
        public void CycleStartIsTheLastCycleDayOnOrBeforeTheDate(string date, int startDay, string expected)
        {
            Assert.Equal(DateOnly.Parse(expected), BudgetRules.GetCycleStart(DateOnly.Parse(date), startDay));
        }

        [Theory]
        [InlineData(0, 20_000_000, 2_000_000, 10)]
        [InlineData(0, 30_000_000, 2_000_000, 15)]
        [InlineData(5_000_000, 20_000_000, 2_000_000, 8)]
        [InlineData(25_000_000, 20_000_000, 2_000_000, 0)]
        public void MonthsToTargetRoundsUp(long balance, long target, long monthly, int expected)
        {
            Assert.Equal(expected, BudgetRules.MonthsToTarget(balance, target, monthly));
        }

        [Fact]
        public void MonthsToTargetIsUnknownWithoutMonthlySaving()
        {
            Assert.Null(BudgetRules.MonthsToTarget(0, 20_000_000, 0));
        }
    }
}
