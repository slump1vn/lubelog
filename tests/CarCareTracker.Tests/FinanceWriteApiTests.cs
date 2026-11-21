using CarCareTracker.External.Interfaces;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarCareTracker.Tests
{
    /// <summary>Rules behind the API v1 writes: idempotent manual entries, budget ETags, receipt matching.</summary>
    public class FinanceWriteApiTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

        private class FakeTransactions : IBankTransactionDataAccess
        {
            public readonly List<BankTransaction> Items = new();
            private int nextId = 1;
            public bool AddIfNew(BankTransaction transaction)
            {
                if (Items.Any(x => x.Fingerprint == transaction.Fingerprint))
                {
                    return false;
                }
                transaction.Id = nextId++;
                Items.Add(transaction);
                return true;
            }
            public List<BankTransaction> GetRecent(int householdId, int limit) => Items.Where(x => x.HouseholdId == householdId).OrderByDescending(x => x.OccurredAtUtc).Take(limit).ToList();
            public List<BankTransaction> GetSince(int householdId, DateTime fromUtc) => Items.Where(x => x.HouseholdId == householdId && x.OccurredAtUtc >= fromUtc).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToList();
            public List<BankTransaction> GetPage(int householdId, DateTime? beforeUtc, int beforeId, int limit) => throw new NotSupportedException();
            public bool Delete(int householdId, int id) => Items.RemoveAll(x => x.HouseholdId == householdId && x.Id == id) > 0;
        }
        private class FakeBudgets : IBudgetDataAccess
        {
            public BudgetProfile? Profile;
            public BudgetProfile? GetBudgetProfile(int householdId) => Profile;
            public bool SaveBudgetProfile(BudgetProfile profile) { Profile = profile; return true; }
            public List<BudgetProfile> GetBudgetProfiles() => Profile == null ? new() : new() { Profile };
            public bool AddAuditEntry(BudgetAuditEntry entry) => true;
            public List<BudgetAuditEntry> GetAuditEntries(int householdId, int limit) => new();
        }

        private static (ReconciliationLogic Logic, FakeTransactions Data, string GapKey) WithGapOf(long unexplained)
        {
            var data = new FakeTransactions();
            data.AddIfNew(new BankTransaction { HouseholdId = -1, Fingerprint = "a", OccurredAtUtc = T0, Direction = TransferDirection.In, Amount = 2_000, Balance = 100_000, Source = "webhook" });
            data.AddIfNew(new BankTransaction { HouseholdId = -1, Fingerprint = "b", OccurredAtUtc = T0.AddDays(1), Direction = TransferDirection.In, Amount = 10_000, Balance = 110_000 - unexplained, Source = "webhook" });
            var logic = new ReconciliationLogic(data, new FakeBudgets(), NullLogger<ReconciliationLogic>.Instance);
            return (logic, data, ReconciliationRules.FindGaps(data.Items).Single().Key);
        }
        private static ManualTransactionInput Input(string gapKey, long amount) => new() { GapKey = gapKey, Amount = amount, Date = "2026-11-12", Category = "Chợ: Ăn uống", Note = "Chợ" };

        [Fact]
        public void ARetryWithTheSameKeyReturnsTheFirstEntry()
        {
            var (logic, data, gap) = WithGapOf(30_000);
            var fingerprint = FinanceApiRules.ManualFingerprint(-1, 3, "0b7e1c2a-key");
            var first = logic.AddManualEntry(-1, 3, Input(gap, 30_000), fingerprint);
            Assert.NotNull(first.Entry);
            Assert.False(first.Replayed);
            //the gap is now explained, yet the retry is not an error: it gets the same entry back.
            var retry = logic.AddManualEntry(-1, 3, Input(gap, 30_000), fingerprint);
            Assert.True(retry.Replayed);
            Assert.Equal(first.Entry!.Id, retry.Entry!.Id);
            Assert.Single(data.Items, x => x.Source == ReconciliationRules.SourceManual);
        }

        [Fact]
        public void AnotherKeyForAnExplainedGapIsAConflict()
        {
            var (logic, _, gap) = WithGapOf(30_000);
            Assert.NotNull(logic.AddManualEntry(-1, 3, Input(gap, 30_000), FinanceApiRules.ManualFingerprint(-1, 3, "key-one-1")).Entry);
            var second = logic.AddManualEntry(-1, 7, Input(gap, 30_000), FinanceApiRules.ManualFingerprint(-1, 7, "key-two-2"));
            Assert.Null(second.Entry);
            Assert.True(second.GapGone);
            Assert.Equal("gap_not_found", FinanceApiRules.IssueCode(second.Issues.Single()));
        }

        [Fact]
        public void AnAmountLargerThanTheGapIsRefused()
        {
            var (logic, _, gap) = WithGapOf(30_000);
            var result = logic.AddManualEntry(-1, 3, Input(gap, 40_000), null);
            Assert.Null(result.Entry);
            Assert.False(result.GapGone);
            Assert.Equal("amount_exceeds_gap", FinanceApiRules.IssueCode(result.Issues.Single()));
        }

        [Fact]
        public void IdempotencyKeysAreScopedAndChecked()
        {
            Assert.NotEqual(FinanceApiRules.ManualFingerprint(-1, 3, "same-key-1"), FinanceApiRules.ManualFingerprint(-1, 7, "same-key-1"));
            Assert.StartsWith("manual:", FinanceApiRules.ManualFingerprint(-1, 3, "same-key-1"));
            Assert.True(FinanceApiRules.IsWellFormedIdempotencyKey(Guid.NewGuid().ToString()));
            Assert.False(FinanceApiRules.IsWellFormedIdempotencyKey("short"));
            Assert.False(FinanceApiRules.IsWellFormedIdempotencyKey("has a space in it"));
            Assert.False(FinanceApiRules.IsWellFormedIdempotencyKey(new string('k', 65)));
        }

        [Fact]
        public void IssueCodesAreStable()
        {
            Assert.Equal("the_note_is_too_long", FinanceApiRules.IssueCode("The note is too long"));
            Assert.Equal("fund_has_no_name", FinanceApiRules.IssueCode("Fund {0} has no name"));
            Assert.Equal("not_configured", FinanceApiRules.IssueCode("The SePay API key is not configured"));
        }

        private static BudgetProfile Profile() => new()
        {
            Id = -1,
            CycleStartDay = 5,
            SharedAccount = new SharedAccount { AccountNumber = "123456789", BankName = "VietinBank" },
            Incomes = { new IncomeSource { Key = "a", Name = "Lương", Amount = 9_000_000, PayDay = 5, Allocations = { new IncomeAllocation { FundKey = "f", Amount = 9_000_000, Order = 1 } } } },
            Funds = { new Fund { Key = "f", Name = "Chung", Kind = FundKind.Shared, MonthlyAmount = 9_000_000, Categories = { new BudgetCategory { Key = "c", Name = "Ăn uống", MonthlyLimit = 9_000_000 } } } },
            UpdatedAtUtc = T0
        };

        [Fact]
        public void TheETagChangesWithTheContentOnly()
        {
            var tag = FinanceApiRules.BudgetETag(Profile());
            Assert.StartsWith("\"", tag);
            Assert.Equal(tag, FinanceApiRules.BudgetETag(Profile()));
            var changed = Profile();
            changed.Funds[0].Categories[0].MonthlyLimit = 6_000_000;
            Assert.NotEqual(tag, FinanceApiRules.BudgetETag(changed));
            Assert.True(FinanceApiRules.IfMatchAccepts(tag, tag));
            Assert.True(FinanceApiRules.IfMatchAccepts("\"other\", " + tag, tag));
            Assert.True(FinanceApiRules.IfMatchAccepts("*", tag));
            Assert.False(FinanceApiRules.IfMatchAccepts("W/" + tag, tag));
            Assert.False(FinanceApiRules.IfMatchAccepts(null, tag));
        }

        [Fact]
        public void ABudgetSentBackKeepsTheJointAccountAndRoundTrips()
        {
            var current = Profile();
            var dto = FinanceApiRules.ToDto(current);
            dto.ApprovalThreshold = 2_000_000;
            var (profile, error) = FinanceApiRules.FromDto(dto, current, -1);
            Assert.Null(error);
            Assert.Equal("123456789", profile!.SharedAccount.AccountNumber);
            Assert.Equal(2_000_000, profile.ApprovalThreshold);
            Assert.Equal(1, profile.Incomes[0].Allocations[0].Order);
            Assert.Empty(BudgetRules.Validate(profile));
            //an unchanged plan gives the same tag once the save stamp is the same.
            var (same, _) = FinanceApiRules.FromDto(FinanceApiRules.ToDto(current), current, -1);
            same!.UpdatedAtUtc = current.UpdatedAtUtc;
            Assert.Equal(FinanceApiRules.BudgetETag(current), FinanceApiRules.BudgetETag(same));
            dto.Funds[0].Kind = "lottery";
            Assert.NotNull(FinanceApiRules.FromDto(dto, current, -1).Error);
        }

        private static BalanceGap Gap(long amount, int day) => new()
        {
            Key = $"g{day}",
            Amount = amount,
            FromUtc = new DateTime(2026, 11, day, 2, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 11, day + 1, 2, 0, 0, DateTimeKind.Utc)
        };

        [Fact]
        public void AReceiptMatchesMoneyOutGapsLargeEnoughAndNearItsDate()
        {
            var gaps = new[] { Gap(-500_000, 10), Gap(-120_000, 10), Gap(-90_000, 10), Gap(200_000, 10), Gap(-130_000, 20) };
            var matches = FinanceApiRules.MatchGaps(gaps, 100_000, new DateOnly(2026, 11, 11));
            Assert.Equal(new[] { "g10", "g10" }, matches.Select(x => x.Key));
            Assert.Equal(new long[] { -120_000, -500_000 }, matches.Select(x => x.Amount));
            //without a date, any period will do.
            Assert.Equal(3, FinanceApiRules.MatchGaps(gaps, 100_000, null).Count);
            Assert.Empty(FinanceApiRules.MatchGaps(gaps, 0, null));
        }

        [Fact]
        public void ATransferAlreadyInTheBankDataIsFound()
        {
            var bank = new List<BankTransaction>
            {
                new() { Id = 1, Direction = TransferDirection.Out, Amount = 250_000, Reference = "FT26316123456", OccurredAtUtc = new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc), Source = "api" },
                new() { Id = 2, Direction = TransferDirection.Out, Amount = 99_000, OccurredAtUtc = new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc), Source = "webhook" },
                new() { Id = 3, Direction = TransferDirection.Out, Amount = 99_000, OccurredAtUtc = new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc), Source = ReconciliationRules.SourceManual }
            };
            Assert.Equal(1, FinanceApiRules.FindRecorded(bank, 250_000, null, "FT26316123456")?.Id);
            //18:00 UTC on the 12th is the 13th in Vietnam: still the day after.
            Assert.Equal(2, FinanceApiRules.FindRecorded(bank, 99_000, new DateOnly(2026, 11, 12), null)?.Id);
            Assert.Null(FinanceApiRules.FindRecorded(bank, 99_000, new DateOnly(2026, 11, 10), null));
            Assert.Null(FinanceApiRules.FindRecorded(bank, 1, new DateOnly(2026, 11, 12), null));
        }

        [Fact]
        public void TheModelsAnswerIsCleaned()
        {
            var today = new DateOnly(2026, 11, 21);
            var reading = ReceiptRules.ToReading(new ReceiptModelAnswer { kind = "transfer", vendor = "NGUYEN VAN A 0123456789", date = "2026-11-20", total = 250000.4m, reference = "FT26316123456", content = "tien cho" }, today);
            Assert.Equal("transfer", reading.Kind);
            Assert.Equal("NGUYEN VAN A ****6789", reading.Vendor);
            Assert.Equal(new DateOnly(2026, 11, 20), reading.Date);
            Assert.Equal(250_000, reading.Total);
            Assert.Equal("FT26316123456", reading.Reference);
            var odd = ReceiptRules.ToReading(new ReceiptModelAnswer { kind = "menu", date = "2031-01-01", total = -5, reference = "X" }, today);
            Assert.Equal("other", odd.Kind);
            Assert.Null(odd.Date);
            Assert.Equal(0, odd.Total);
            Assert.Equal(string.Empty, odd.Reference);
        }

        [Fact]
        public void SessionsShowTheirKind()
        {
            var dto = FinanceApiRules.ToDto(new UserSessionViewModel { Id = 4, Kind = SessionKind.App, DeviceName = "Pixel", IsCurrent = true, CreatedUtc = T0, LastSeenUtc = T0, ExpiresUtc = T0 });
            Assert.Equal("app", dto.Kind);
            Assert.True(dto.IsCurrent);
            Assert.Equal(DateTimeKind.Utc, dto.LastSeenUtc.Kind);
        }
    }
}
