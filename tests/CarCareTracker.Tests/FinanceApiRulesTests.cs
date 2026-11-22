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
