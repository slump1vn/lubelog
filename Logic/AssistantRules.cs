using CarCareTracker.Helper;
using CarCareTracker.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Pure rules behind the assistant: picking a read-only tool from a message, budget cycle ranges, and
    /// the numbers of each answer. Nothing here talks to the AI, and the AI never computes a number.
    /// </summary>
    public static class AssistantRules
    {
        /// <summary>Messages (text or photo) a user may send per minute.</summary>
        public const int RequestsPerMinute = 20;
        /// <summary>Chat history is deleted after this many days.</summary>
        public const int RetentionDays = 30;
        public const int MaxMessageLength = 1000;
        public const int MaxCount = 20;
        public const int MaxKeywordLength = 40;
        public const int SearchDays = 90;
        public const int HistoryPageSize = 50;

        private static bool Has(string text, params string[] phrases)
        {
            return phrases.Any(phrase => Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])"));
        }

        //words that mean the message is about a vehicle, not the family money.
        private static readonly string[] VehicleWords = new[]
        {
            "xe", "nhot", "thay dau", "dau may", "ac quy", "lop", "bao duong", "xang", "do xang", "odo", "km", "bugi", "phanh", "dang kiem",
            "gui xe", "rua xe", "phat nguoi", "loc gio", "curoa", "sac dien"
        };

        /// <summary>The category labels the message names, best (longest) match first.</summary>
        public static string FindCategory(string normalized, IEnumerable<string> categories)
        {
            var best = string.Empty;
            int bestLength = 0;
            foreach (var category in categories)
            {
                //"Ăn uống: Đi chợ" can be named by its own part ("đi chợ") or by the fund ("ăn uống").
                var parts = category.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(VietnameseTextParser.Normalize)
                    .Where(x => x.Length >= 3)
                    .ToList();
                var own = parts.LastOrDefault() ?? string.Empty;
                foreach (var candidate in new[] { VietnameseTextParser.Normalize(category), own }.Where(x => x.Length >= 3))
                {
                    if (Has(normalized, candidate) && candidate.Length > bestLength)
                    {
                        best = category;
                        bestLength = candidate.Length;
                    }
                }
            }
            if (!string.IsNullOrEmpty(best))
            {
                return best;
            }
            //only the fund was named: the fund itself when it has no categories.
            return categories.FirstOrDefault(x => !x.Contains(':') && Has(normalized, VietnameseTextParser.Normalize(x))) ?? string.Empty;
        }

        /// <summary>
        /// Picks a tool with keyword rules. Returns <see cref="AssistantTool.Unknown"/> when unsure, so the
        /// home AI (or the vehicle assistant) can decide.
        /// </summary>
        public static AssistantRoute Route(string message, IEnumerable<string> categories, IEnumerable<string> vehicleNames)
        {
            var text = VietnameseTextParser.Normalize(message);
            var route = new AssistantRoute();
            if (string.IsNullOrEmpty(text))
            {
                return route;
            }
            route.Period = Has(text, "thang truoc", "ky truoc", "chu ky truoc", "last month", "previous month") ? AssistantPeriod.Previous : AssistantPeriod.Current;
            if (Has(text, "tien vao", "nhan duoc", "thu nhap", "tien ve", "money in", "income"))
            {
                route.Direction = TransferDirection.In;
            }
            else if (Has(text, "tien ra", "chuyen di", "da chi", "money out"))
            {
                route.Direction = TransferDirection.Out;
            }
            var count = Regex.Match(text, @"(?<![0-9])(\d{1,2})\s*(giao dich|khoan|transactions?)");
            if (count.Success)
            {
                route.Count = Math.Clamp(int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture), 1, MaxCount);
            }
            bool aboutVehicle = Has(text, VehicleWords) || vehicleNames.Select(VietnameseTextParser.Normalize).Where(x => x.Length >= 2).Any(x => Has(text, x));

            if (Has(text, "ban lam duoc gi", "lam duoc gi", "giup gi", "huong dan", "help", "tro giup", "co the hoi gi"))
            {
                route.Tool = AssistantTool.Help;
                return route;
            }
            if (Has(text, "so du", "con bao nhieu tien", "tai khoan chung", "trong tai khoan", "balance"))
            {
                route.Tool = AssistantTool.Balance;
                return route;
            }
            var search = Regex.Match(text, @"(?<![a-z0-9])(tim|tra cuu|search)(\s+(cac|nhung))?(\s+(giao dich|khoan|chi))?(\s+(co|chua|ve|cho))?\s+(?<word>.+)$");
            if (search.Success)
            {
                var keyword = CleanKeyword(search.Groups["word"].Value);
                if (!string.IsNullOrEmpty(keyword))
                {
                    route.Tool = AssistantTool.Search;
                    route.Keyword = keyword;
                    return route;
                }
            }
            //a named budget category wins over vehicle words in a question ("xăng xe tháng này hết bao nhiêu?"),
            //but a message with numbers is a log entry for the vehicle assistant ("đổ xăng 800k").
            var category = FindCategory(text, categories);
            bool hasNumber = Regex.IsMatch(text, @"\d");
            if (!string.IsNullOrEmpty(category) && category != "Other" && !(aboutVehicle && hasNumber))
            {
                route.Tool = AssistantTool.Category;
                route.Category = category;
                return route;
            }
            if (aboutVehicle)
            {
                route.Tool = AssistantTool.Vehicle;
                return route;
            }
            if (Has(text, "sap toi", "sap phai tra", "sap tra", "den han", "ngay luong", "khi nao co luong", "bao gio co luong", "tra no", "ky tra", "upcoming"))
            {
                route.Tool = AssistantTool.Upcoming;
                return route;
            }
            if (Has(text, "giao dich", "gan day", "moi nhat", "lich su giao dich", "vua chuyen", "transactions"))
            {
                route.Tool = AssistantTool.Recent;
                return route;
            }
            if (Has(text, "chi tieu", "tieu bao nhieu", "chi bao nhieu", "tieu het", "chi het", "tong chi", "da tieu", "da chi", "thu chi", "tien vao", "tien ra", "thu nhap", "nhan duoc", "spending", "spent"))
            {
                route.Tool = AssistantTool.CycleSummary;
                return route;
            }
            return route;
        }

        /// <summary>Keeps a search word short and plain; it is only ever compared, never sent anywhere.</summary>
        public static string CleanKeyword(string? keyword)
        {
            var text = VietnameseTextParser.Normalize(keyword ?? string.Empty);
            text = Regex.Replace(text, @"[^a-z0-9 ]", " ");
            text = Regex.Replace(text, @"\b(thang nay|thang truoc|ky nay|ky truoc|gan day|nao|khong|giup|minh|voi|di|nhe|a)\b", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length > MaxKeywordLength ? text.Substring(0, MaxKeywordLength).Trim() : text;
        }

        /// <summary>Checks a route the AI proposed: unknown categories, odd counts and long words are dropped.</summary>
        public static AssistantRoute Sanitize(AssistantRoute route, IEnumerable<string> categories)
        {
            route.Count = Math.Clamp(route.Count, 1, MaxCount);
            route.Keyword = CleanKeyword(route.Keyword);
            if (route.Tool == AssistantTool.Category && !categories.Contains(route.Category))
            {
                route.Tool = AssistantTool.CycleSummary;
                route.Category = string.Empty;
            }
            if (route.Tool == AssistantTool.Search && string.IsNullOrEmpty(route.Keyword))
            {
                route.Tool = AssistantTool.Recent;
            }
            return route;
        }

        public static AssistantTool ParseTool(string? tool)
        {
            return (tool ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "balance" => AssistantTool.Balance,
                "cycle_summary" => AssistantTool.CycleSummary,
                "category" => AssistantTool.Category,
                "upcoming" => AssistantTool.Upcoming,
                "recent" => AssistantTool.Recent,
                "search" => AssistantTool.Search,
                "vehicle" => AssistantTool.Vehicle,
                "help" => AssistantTool.Help,
                _ => AssistantTool.Unknown
            };
        }
        public static string ToolName(AssistantTool tool)
        {
            return tool switch
            {
                AssistantTool.Balance => "balance",
                AssistantTool.CycleSummary => "cycle_summary",
                AssistantTool.Category => "category",
                AssistantTool.Upcoming => "upcoming",
                AssistantTool.Recent => "recent",
                AssistantTool.Search => "search",
                AssistantTool.Vehicle => "vehicle",
                AssistantTool.Help => "help",
                _ => string.Empty
            };
        }

        /// <summary>First and last day (inclusive) of the asked budget cycle.</summary>
        public static (DateOnly From, DateOnly To) PeriodRange(DateOnly today, int cycleStartDay, AssistantPeriod period)
        {
            var start = BudgetRules.GetCycleStart(today, cycleStartDay);
            if (period == AssistantPeriod.Previous)
            {
                var previousStart = BudgetRules.GetCycleStart(start.AddDays(-1), cycleStartDay);
                return (previousStart, start.AddDays(-1));
            }
            return (start, DashboardRules.GetCycleEnd(start, cycleStartDay));
        }
        public static DateTime StartUtc(DateOnly vietnamDate)
        {
            return DateTime.SpecifyKind(vietnamDate.ToDateTime(TimeOnly.MinValue) - SepayRules.VietnamOffset, DateTimeKind.Utc);
        }
        /// <summary>Transactions booked between the two Vietnam dates, both inclusive.</summary>
        public static List<BankTransaction> InRange(IEnumerable<BankTransaction> transactions, DateOnly from, DateOnly to)
        {
            var fromUtc = StartUtc(from);
            var toUtc = StartUtc(to.AddDays(1));
            return transactions.Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc < toUtc).ToList();
        }

        public class PeriodTotals
        {
            public long In { get; set; }
            public long Out { get; set; }
            /// <summary>Part of Out found only from the bank balance (no transaction reported).</summary>
            public long UnrecordedOut { get; set; }
            /// <summary>Money out without a category, recorded transactions only.</summary>
            public long Uncategorized { get; set; }
            /// <summary>Money out per category, biggest first.</summary>
            public List<(string Category, long Amount)> ByCategory { get; set; } = new List<(string, long)>();
        }
        /// <summary>Totals of a list that already includes the balance gaps (see ReconciliationRules.WithGaps).</summary>
        public static PeriodTotals Totals(IEnumerable<BankTransaction> transactions)
        {
            var totals = new PeriodTotals();
            var spending = new Dictionary<string, long>();
            foreach (var transaction in transactions)
            {
                if (transaction.Direction == TransferDirection.In)
                {
                    totals.In += transaction.Amount;
                    continue;
                }
                totals.Out += transaction.Amount;
                if (transaction.Source == ReconciliationRules.SourceGap)
                {
                    totals.UnrecordedOut += transaction.Amount;
                }
                else if (string.IsNullOrWhiteSpace(transaction.Category))
                {
                    totals.Uncategorized += transaction.Amount;
                }
                else
                {
                    spending[transaction.Category] = spending.GetValueOrDefault(transaction.Category) + transaction.Amount;
                }
            }
            totals.ByCategory = spending.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
            return totals;
        }

        /// <summary>Monthly limit of a category label ("Fund: Category" or a fund without categories); null when none is set.</summary>
        public static long? MonthlyLimit(BudgetProfile? profile, string category)
        {
            foreach (var fund in profile?.Funds ?? new List<Fund>())
            {
                if (fund.Categories.Any())
                {
                    var match = fund.Categories.FirstOrDefault(x => $"{fund.Name}: {x.Name}" == category);
                    if (match != null)
                    {
                        return match.MonthlyLimit > 0 ? match.MonthlyLimit : null;
                    }
                }
                else if (fund.Name == category)
                {
                    return fund.MonthlyAmount > 0 ? fund.MonthlyAmount : null;
                }
            }
            return null;
        }

        /// <summary>Transactions whose content or note contains every word of the keyword, newest first.</summary>
        public static List<BankTransaction> Search(IEnumerable<BankTransaction> transactions, string keyword)
        {
            var words = CleanKeyword(keyword).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!words.Any())
            {
                return new List<BankTransaction>();
            }
            return transactions
                .Where(x => x.Source != ReconciliationRules.SourceGap)
                .Where(x =>
                {
                    var haystack = VietnameseTextParser.Normalize($"{x.Content} {x.Note} {x.Category}");
                    return words.All(word => haystack.Contains(word));
                })
                .OrderByDescending(x => x.OccurredAtUtc)
                .ToList();
        }

        /// <summary>Questions shown as buttons when the chat opens.</summary>
        public static List<string> StarterSuggestions(bool hasVehicles)
        {
            var list = new List<string>
            {
                "Tài khoản chung còn bao nhiêu?",
                "Tháng này chi bao nhiêu?",
                "Sắp tới phải trả gì?",
                "5 giao dịch gần nhất"
            };
            if (hasVehicles)
            {
                list.Add("Bao giờ phải bảo dưỡng xe?");
            }
            return list;
        }
        /// <summary>Follow-up buttons after an answer.</summary>
        public static List<string> FollowUps(AssistantTool tool, AssistantPeriod period)
        {
            return tool switch
            {
                AssistantTool.Balance => new List<string> { "Tháng này chi bao nhiêu?", "Sắp tới phải trả gì?" },
                AssistantTool.CycleSummary => period == AssistantPeriod.Current
                    ? new List<string> { "Tháng trước chi bao nhiêu?", "5 giao dịch gần nhất" }
                    : new List<string> { "Tháng này chi bao nhiêu?" },
                AssistantTool.Category => new List<string> { "Tháng này chi bao nhiêu?" },
                AssistantTool.Upcoming => new List<string> { "Tài khoản chung còn bao nhiêu?" },
                AssistantTool.Recent => new List<string> { "10 giao dịch gần nhất", "Tháng này chi bao nhiêu?" },
                AssistantTool.Search => new List<string> { "5 giao dịch gần nhất" },
                _ => StarterSuggestions(false)
            };
        }

        /// <summary>Photo hint from the text sent with it: food label, or receipt/transfer (default).</summary>
        public static bool WantsFoodLabel(string? hint)
        {
            var text = VietnameseTextParser.Normalize(hint ?? string.Empty);
            return text == "food" || Has(text, "nhan", "thuc pham", "thanh phan", "dinh duong", "do an", "food", "label");
        }
    }
}
