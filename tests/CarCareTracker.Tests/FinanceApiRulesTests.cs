using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class FinanceApiRulesTests
    {
        private static BankTransaction Tx(int id, int minute) => new()
        {
            Id = id,
            Amount = 1000,
            Direction = TransferDirection.Out,
            OccurredAtUtc = new DateTime(2026, 11, 12, 3, minute, 0, DateTimeKind.Utc)
        };

        [Fact]
        public void CursorPointsAtTheLastItemOfAFullPage()
        {
            var page = new List<BankTransaction> { Tx(9, 30), Tx(7, 10) };
            var cursor = FinanceApiRules.NextCursor(page, 2);
            Assert.NotNull(cursor);
            Assert.True(FinanceApiRules.TryParseCursor(cursor, out var before, out int beforeId));
            Assert.Equal(page[1].OccurredAtUtc, before);
            Assert.Equal(DateTimeKind.Utc, before!.Value.Kind);
            Assert.Equal(7, beforeId);
            //a short page is the last one.
            Assert.Null(FinanceApiRules.NextCursor(page, 3));
        }

        [Fact]
        public void BadCursorsAreRejectedAndNoCursorMeansTheFirstPage()
        {
            Assert.True(FinanceApiRules.TryParseCursor(null, out var before, out _));
            Assert.Null(before);
            Assert.False(FinanceApiRules.TryParseCursor("abc", out _, out _));
            Assert.False(FinanceApiRules.TryParseCursor("-5.3", out _, out _));
            Assert.False(FinanceApiRules.TryParseCursor("99999999999999999999.1", out _, out _));
        }

        [Fact]
        public void BudgetForTheAppsLeavesOutTheAccountNumberAndSortsByDay()
        {
            var profile = new BudgetProfile
            {
                Id = -1,
                CycleStartDay = 5,
                ApprovalThreshold = 1_000_000,
                SharedAccount = new SharedAccount { AccountNumber = "123456789", BankName = "VietinBank" },
                Incomes = new()
                {
                    new IncomeSource { Key = "b", Name = "Lương 2", Amount = 16_000_000, PayDay = 20 },
                    new IncomeSource
                    {
                        Key = "a", Name = "Lương 1", Amount = 9_000_000, PayDay = 5,
                        Allocations = new() { new IncomeAllocation { FundKey = "debt", Amount = 8_000_000, Order = 2 }, new IncomeAllocation { FundKey = "personal", Amount = 1_000_000, Order = 1 } }
                    }
                },
                Funds = new()
                {
                    new Fund { Key = "shared", Kind = FundKind.Shared, MonthlyAmount = 10_000_000, OwnerUserId = 3, Categories = new() { new BudgetCategory { Key = "food", Name = "Ăn uống", MonthlyLimit = 6_000_000 } } },
                    new Fund { Key = "sos", Kind = FundKind.Emergency, TargetAmount = 25_000_000 },
                    new Fund { Key = "soi", Kind = FundKind.Personal, OwnerUserId = 7 }
                },
                Debts = new() { new Debt { Key = "late", DueDay = 21 }, new Debt { Key = "early", DueDay = 3, RemainingBalance = null } }
            };
            var dto = FinanceApiRules.ToDto(profile);
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            Assert.DoesNotContain("123456789", json);
            Assert.Equal(new[] { 5, 20 }, dto.Incomes.Select(x => x.PayDay));
            Assert.Equal(new[] { "personal", "debt" }, dto.Incomes[0].Allocations.Select(x => x.FundKey));
            Assert.Equal(new[] { "early", "late" }, dto.Debts.Select(x => x.Key));
            Assert.Equal(new[] { "shared", "emergency", "personal" }, dto.Funds.Select(x => x.Kind));
            Assert.Null(dto.Funds[0].TargetAmount);
            Assert.Null(dto.Funds[0].OwnerUserId);
            Assert.Equal(25_000_000, dto.Funds[1].TargetAmount);
            Assert.Equal(7, dto.Funds[2].OwnerUserId);
            Assert.Equal(6_000_000, dto.Funds[0].Categories.Single().MonthlyLimit);
        }

        [Fact]
        public void PageSizeStaysWithinLimits()
        {
            Assert.Equal(50, FinanceApiRules.ClampPageSize(null));
            Assert.Equal(1, FinanceApiRules.ClampPageSize(0));
            Assert.Equal(200, FinanceApiRules.ClampPageSize(5000));
        }

        [Fact]
        public void DashboardShowsOnlyTheLastDigitsOfTheAccount()
        {
            var model = new FinanceDashboardViewModel
            {
                Account = new SharedAccountViewModel
                {
                    Account = new SharedAccount { AccountNumber = "0123456789", BankName = "VCB" },
                    Balance = 5_000_000,
                    CycleIn = 9_000_000,
                    CycleOut = 4_000_000,
                    Recent = Enumerable.Range(1, 12).Select(i => Tx(i, i)).ToList()
                },
                Upcoming = { new UpcomingMoney { Name = "Lương", Amount = 16_000_000, Direction = TransferDirection.In } }
            };
            var dto = FinanceApiRules.ToDto(model);
            Assert.Equal("•••• 6789", dto.Account.MaskedNumber);
            Assert.True(dto.Account.Linked);
            Assert.Null(dto.Account.LastSyncSucceeded);
            Assert.Equal(5_000_000, dto.Cycle.Net);
            Assert.Equal(8, dto.Recent.Count);
            Assert.Equal("out", dto.Recent[0].Direction);
            Assert.Equal("in", dto.Upcoming[0].Direction);
            Assert.Equal("123", FinanceApiRules.MaskAccountNumber("123"));
        }

        [Fact]
        public void OnlyWellFormedIosAppIdsAreListed()
        {
            Assert.Equal(new[] { "ABCDE12345.vn.digifusion.finance" }, IosAppLinks.ParseAppIds("ABCDE12345.vn.digifusion.finance, bad, ABCDE12345.vn.digifusion.finance"));
            Assert.Empty(IosAppLinks.ParseAppIds(null));
            Assert.Empty(IosAppLinks.ParseAppIds("abcde12345.vn.digifusion.finance"));
        }
    }
}
