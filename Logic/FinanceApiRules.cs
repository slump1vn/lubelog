using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using System.Globalization;

namespace CarCareTracker.Logic
{
    /// <summary>Maps the finance figures to the API v1 shapes, and reads and writes page cursors.</summary>
    public static class FinanceApiRules
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        public static string MaskAccountNumber(string? number)
        {
            number ??= string.Empty;
            return number.Length > 4 ? "•••• " + number[^4..] : number;
        }
        public static string Direction(TransferDirection direction) => direction == TransferDirection.In ? "in" : "out";
        public static TransactionDto ToDto(BankTransaction x) => new()
        {
            Id = x.Id,
            OccurredAtUtc = DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc),
            Direction = Direction(x.Direction),
            Amount = x.Amount,
            Balance = x.Balance,
            Content = x.Content,
            Reference = x.Reference
        };
        public static DashboardDto ToDto(FinanceDashboardViewModel model, int recentCount = 8)
        {
            var account = model.Account;
            return new DashboardDto
            {
                Today = model.Today,
                Account = new AccountDto
                {
                    Linked = !string.IsNullOrEmpty(account.Account.AccountNumber),
                    BankName = account.Account.BankName,
                    MaskedNumber = MaskAccountNumber(account.Account.AccountNumber),
                    Balance = account.Balance,
                    LastSyncUtc = account.LastSync?.LastRunUtc is DateTime run ? DateTime.SpecifyKind(run.ToUniversalTime(), DateTimeKind.Utc) : null,
                    LastSyncSucceeded = account.LastSync?.LastRunUtc == null ? null : account.LastSync.Succeeded
                },
                Cycle = new CycleDto
                {
                    Start = account.CycleStart,
                    End = model.CycleEnd,
                    Day = model.DayOfCycle,
                    Days = model.DaysInCycle,
                    MoneyIn = account.CycleIn,
                    MoneyOut = account.CycleOut,
                    Net = model.Net,
                    ProjectedOut = model.ProjectedOut
                },
                BalanceHistory = model.BalanceHistory.Select(x => new BalancePointDto { Date = x.Date, Balance = x.Balance }).ToList(),
                Upcoming = model.Upcoming.Select(x => new UpcomingDto { Date = x.Date, Name = x.Name, Amount = x.Amount, Direction = Direction(x.Direction) }).ToList(),
                Recent = account.Recent.Take(recentCount).Select(ToDto).ToList()
            };
        }
        /// <summary>The cursor is "ticks.id" of the last item on a page: opaque to the app, stable on the server.</summary>
        public static string? NextCursor(IReadOnlyList<BankTransaction> page, int limit)
        {
            if (page.Count < limit || page.Count == 0)
            {
                return null;
            }
            var last = page[^1];
            return string.Create(CultureInfo.InvariantCulture, $"{DateTime.SpecifyKind(last.OccurredAtUtc, DateTimeKind.Utc).Ticks}.{last.Id}");
        }
        public static bool TryParseCursor(string? cursor, out DateTime? beforeUtc, out int beforeId)
        {
            beforeUtc = null;
            beforeId = 0;
            if (string.IsNullOrEmpty(cursor))
            {
                return true;
            }
            var parts = cursor.Split('.');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out beforeId)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                beforeId = 0;
                return false;
            }
            beforeUtc = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        public static int ClampPageSize(int? limit)
        {
            return Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        }
    }
}
