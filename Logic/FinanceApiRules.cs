using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
            Reference = x.Reference,
            Source = x.Source,
            Category = x.Category,
            Note = x.Note
        };
        public static string FundKind(FundKind kind) => kind switch
        {
            Models.FundKind.Personal => "personal",
            Models.FundKind.Debt => "debt",
            Models.FundKind.Emergency => "emergency",
            Models.FundKind.Savings => "savings",
            _ => "shared"
        };
        /// <summary>The budget plan for the apps: no account number, no internal ids, zeros as null where 0 means "none".</summary>
        public static BudgetDto ToDto(BudgetProfile profile) => new()
        {
            Currency = profile.Currency,
            ApprovalThreshold = profile.ApprovalThreshold,
            CycleStartDay = profile.CycleStartDay,
            Incomes = profile.Incomes.OrderBy(x => x.PayDay).Select(x => new BudgetIncomeDto
            {
                Key = x.Key,
                Name = x.Name,
                Amount = x.Amount,
                PayDay = x.PayDay,
                Allocations = x.Allocations.OrderBy(a => a.Order).Select(a => new BudgetAllocationDto { FundKey = a.FundKey, Amount = a.Amount }).ToList()
            }).ToList(),
            Funds = profile.Funds.Select(x => new BudgetFundDto
            {
                Key = x.Key,
                Name = x.Name,
                Kind = FundKind(x.Kind),
                MonthlyAmount = x.MonthlyAmount,
                TargetAmount = x.TargetAmount > 0 ? x.TargetAmount : null,
                OwnerUserId = x.Kind == Models.FundKind.Personal && x.OwnerUserId != 0 ? x.OwnerUserId : null,
                Categories = x.Categories.Select(c => new BudgetCategoryDto { Key = c.Key, Name = c.Name, MonthlyLimit = c.MonthlyLimit }).ToList()
            }).ToList(),
            Debts = profile.Debts.OrderBy(x => x.DueDay).Select(x => new BudgetDebtDto
            {
                Key = x.Key,
                Name = x.Name,
                MonthlyPayment = x.MonthlyPayment,
                DueDay = x.DueDay,
                RemainingBalance = x.RemainingBalance,
                AnnualInterestRate = x.AnnualInterestRate,
                FundKey = x.FundKey
            }).ToList(),
            UpdatedAtUtc = DateTime.SpecifyKind(profile.UpdatedAtUtc, DateTimeKind.Utc)
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
                    UnrecordedIn = account.CycleUnrecordedIn,
                    UnrecordedOut = account.CycleUnrecordedOut,
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
        public static BalanceGapDto ToDto(BalanceGap x) => new()
        {
            Key = x.Key,
            FromUtc = DateTime.SpecifyKind(x.FromUtc, DateTimeKind.Utc),
            ToUtc = DateTime.SpecifyKind(x.ToUtc, DateTimeKind.Utc),
            BalanceBefore = x.BalanceBefore,
            BalanceAfter = x.BalanceAfter,
            Amount = x.AbsoluteAmount,
            Direction = Direction(x.Direction),
            ManualEntries = x.ManualEntries
        };
        public static ReconciliationDto ToDto(ReconciliationViewModel x) => new()
        {
            HasAccount = x.HasAccount,
            Gaps = x.Gaps.Select(ToDto).ToList(),
            ManualEntries = x.ManualEntries.Select(ToDto).ToList(),
            Categories = x.Categories
        };
        public static SessionDto ToDto(UserSessionViewModel x) => new()
        {
            Id = x.Id,
            Kind = x.Kind == SessionKind.App ? "app" : "browser",
            DeviceName = x.DeviceName,
            CreatedUtc = DateTime.SpecifyKind(x.CreatedUtc, DateTimeKind.Utc),
            LastSeenUtc = DateTime.SpecifyKind(x.LastSeenUtc, DateTimeKind.Utc),
            ExpiresUtc = DateTime.SpecifyKind(x.ExpiresUtc, DateTimeKind.Utc),
            IsCurrent = x.IsCurrent
        };

        /// <summary>
        /// Stable code for an English message of the business rules, so the apps can show it in their
        /// own language: "The note is too long" becomes "the_note_is_too_long". Placeholders are dropped.
        /// </summary>
        public static string IssueCode(string message)
        {
            var known = message switch
            {
                "This difference is already explained or no longer exists. Reload the page." => "gap_not_found",
                "The amount is more than the unexplained difference" => "amount_exceeds_gap",
                "Only entries made by hand can be deleted" => "not_manual",
                "The SePay API key is not configured" => "not_configured",
                "No shared account number is saved" => "no_account",
                "SePay does not list this account number" => "account_not_in_sepay",
                _ => null
            };
            if (known != null)
            {
                return known;
            }
            var words = Regex.Replace(Regex.Replace(message, @"\{\d+\}", " "), @"[^A-Za-z0-9]+", "_").Trim('_').ToLowerInvariant();
            return string.IsNullOrEmpty(words) ? "invalid" : words.Length > 60 ? words[..60].TrimEnd('_') : words;
        }

        /// <summary>
        /// The idempotency key of a manual entry becomes its fingerprint, so a retry finds the first entry
        /// instead of adding a second one, also after a restart. Scoped to the user; the key is hashed.
        /// </summary>
        public static string ManualFingerprint(int householdId, int userId, string idempotencyKey)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{householdId}:{userId}:{idempotencyKey}"));
            return "manual:" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }
        /// <summary>Idempotency keys are what the app makes: a UUID or similar, printable, 8 to 64 characters.</summary>
        public static bool IsWellFormedIdempotencyKey(string? key)
        {
            return !string.IsNullOrEmpty(key) && key.Length is >= 8 and <= 64 && key.All(c => c is >= '!' and <= '~' && c != '"');
        }

        /// <summary>
        /// Strong entity tag of the budget, from its content (RFC 9110 section 8.8.3). Two saves with the
        /// same content give the same tag, any change gives another.
        /// </summary>
        public static string BudgetETag(BudgetProfile profile)
        {
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile));
            return "\"" + Convert.ToBase64String(hash, 0, 18).Replace('+', '-').Replace('/', '_') + "\"";
        }
        /// <summary>True when an If-Match header value names the current tag (or is "*").</summary>
        public static bool IfMatchAccepts(string? ifMatch, string currentETag)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return false;
            }
            //If-Match uses the strong comparison: a weak tag never matches.
            return ifMatch.Split(',').Select(x => x.Trim()).Any(x => x == "*" || x == currentETag);
        }
        public static FundKind? ParseFundKind(string? kind) => kind switch
        {
            "shared" => Models.FundKind.Shared,
            "personal" => Models.FundKind.Personal,
            "debt" => Models.FundKind.Debt,
            "emergency" => Models.FundKind.Emergency,
            "savings" => Models.FundKind.Savings,
            _ => null
        };
        /// <summary>
        /// The budget an app sends back, as a profile. PUT replaces the plan, but the joint account is not
        /// part of the API, so it is kept from the stored profile. Allocation order is the list order.
        /// </summary>
        public static (BudgetProfile? Profile, string? Error) FromDto(BudgetDto dto, BudgetProfile? current, int householdId)
        {
            var funds = new List<Fund>();
            foreach (var x in dto.Funds ?? new())
            {
                var kind = ParseFundKind(x.Kind);
                if (kind == null)
                {
                    return (null, $"Unknown fund kind \"{x.Kind}\"");
                }
                funds.Add(new Fund
                {
                    Key = x.Key ?? string.Empty,
                    Name = x.Name ?? string.Empty,
                    Kind = kind.Value,
                    MonthlyAmount = x.MonthlyAmount,
                    TargetAmount = x.TargetAmount ?? 0,
                    OwnerUserId = kind == Models.FundKind.Personal ? x.OwnerUserId ?? 0 : 0,
                    Categories = (x.Categories ?? new()).Select(c => new BudgetCategory { Key = c.Key ?? string.Empty, Name = c.Name ?? string.Empty, MonthlyLimit = c.MonthlyLimit }).ToList()
                });
            }
            var profile = new BudgetProfile
            {
                Id = householdId,
                Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "VND" : dto.Currency,
                ApprovalThreshold = dto.ApprovalThreshold,
                CycleStartDay = dto.CycleStartDay,
                Incomes = (dto.Incomes ?? new()).Select(x => new IncomeSource
                {
                    Key = x.Key ?? string.Empty,
                    Name = x.Name ?? string.Empty,
                    Amount = x.Amount,
                    PayDay = x.PayDay,
                    Allocations = (x.Allocations ?? new()).Select((a, i) => new IncomeAllocation { FundKey = a.FundKey ?? string.Empty, Amount = a.Amount, Order = i + 1 }).ToList()
                }).ToList(),
                Funds = funds,
                Debts = (dto.Debts ?? new()).Select(x => new Debt
                {
                    Key = x.Key ?? string.Empty,
                    Name = x.Name ?? string.Empty,
                    MonthlyPayment = x.MonthlyPayment,
                    DueDay = x.DueDay,
                    RemainingBalance = x.RemainingBalance,
                    AnnualInterestRate = x.AnnualInterestRate,
                    FundKey = x.FundKey ?? string.Empty
                }).ToList(),
                SharedAccount = current?.SharedAccount ?? new SharedAccount()
            };
            return (profile, null);
        }

        /// <summary>
        /// Money-out gaps a paid amount may explain: the gap is at least the amount, and the payment date
        /// (Vietnam time) falls within three days of the gap's period. Closest amount first.
        /// </summary>
        public static List<BalanceGap> MatchGaps(IEnumerable<BalanceGap> gaps, long amount, DateOnly? date)
        {
            if (amount <= 0)
            {
                return new List<BalanceGap>();
            }
            return gaps.Where(g => g.Direction == TransferDirection.Out && g.AbsoluteAmount >= amount)
                .Where(g => date == null || Overlaps(g, date.Value, 3))
                .OrderBy(g => g.AbsoluteAmount - amount)
                .ThenByDescending(g => g.ToUtc)
                .Take(5)
                .ToList();
        }
        private static bool Overlaps(BalanceGap gap, DateOnly date, int slackDays)
        {
            var from = DateOnly.FromDateTime(gap.FromUtc + SepayRules.VietnamOffset).AddDays(-slackDays);
            var to = DateOnly.FromDateTime(gap.ToUtc + SepayRules.VietnamOffset).AddDays(slackDays);
            return date >= from && date <= to;
        }
        /// <summary>
        /// A bank transaction that already records a payment: same amount going out, and the same
        /// transaction code, or (without a code) booked on the same day or the day after.
        /// </summary>
        public static BankTransaction? FindRecorded(IEnumerable<BankTransaction> transactions, long amount, DateOnly? date, string? reference)
        {
            if (amount <= 0)
            {
                return null;
            }
            var candidates = transactions.Where(x => x.Direction == TransferDirection.Out && x.Amount == amount && x.Source != ReconciliationRules.SourceManual).ToList();
            if (!string.IsNullOrWhiteSpace(reference))
            {
                var byReference = candidates.FirstOrDefault(x => (x.Reference ?? string.Empty).Equals(reference, StringComparison.OrdinalIgnoreCase) || (x.Content ?? string.Empty).Contains(reference, StringComparison.OrdinalIgnoreCase));
                if (byReference != null)
                {
                    return byReference;
                }
            }
            if (date == null)
            {
                return null;
            }
            return candidates.Where(x =>
            {
                var day = DateOnly.FromDateTime(x.OccurredAtUtc + SepayRules.VietnamOffset);
                return day >= date.Value && day <= date.Value.AddDays(1);
            }).OrderBy(x => x.OccurredAtUtc).FirstOrDefault();
        }
        public static int ClampPageSize(int? limit)
        {
            return Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        }
    }
}
