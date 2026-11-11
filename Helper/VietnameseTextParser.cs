using CarCareTracker.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CarCareTracker.Helper
{
    public class ParsedMessage
    {
        public AiIntent Intent { get; set; } = AiIntent.Unknown;
        /// <summary>
        /// 0 when no vehicle, or more than one vehicle, matched.
        /// </summary>
        public int VehicleId { get; set; }
        public DateTime? Date { get; set; }
        public int? Mileage { get; set; }
        public decimal? Cost { get; set; }
        public decimal? FuelAmount { get; set; }
        public List<string> MaintenanceTags { get; set; } = new List<string>();
        public List<string> ExpenseTags { get; set; } = new List<string>();
        public bool MentionsLastMonth { get; set; }
        public bool MentionsThisYear { get; set; }
    }
    /// <summary>
    /// Deterministic extraction of vehicle, date, odometer, fuel amount, cost and items from an informal Vietnamese message,
    /// e.g. "hôm qua thay nhớt Vision ở 12.350km hết 120k, có thay lọc".
    /// Works with or without diacritics. Used on its own when no model is configured, and to check the model's numbers otherwise.
    /// </summary>
    public static class VietnameseTextParser
    {
        private const string Number = @"\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?";
        //longer phrases first; a matched phrase is removed so that shorter ones cannot match inside it.
        private static readonly (string phrase, string tag)[] MaintenancePhrases = new[]
        {
            ("loc gio dieu hoa", "loc-gio-dieu-hoa"), ("loc gio may lanh", "loc-gio-dieu-hoa"), ("loc dieu hoa", "loc-gio-dieu-hoa"),
            ("loc gio", "loc-gio"),
            ("dau hop so", "dau-hop-so"), ("nhot hop so", "dau-hop-so"), ("dau so", "dau-hop-so"),
            ("nhot lap", "nhot-lap"), ("dau lap", "nhot-lap"),
            ("dau phanh", "dau-phanh"), ("dau thang", "dau-phanh"),
            ("ma phanh", "ma-phanh"), ("bo thang", "ma-phanh"), ("bo phanh", "ma-phanh"),
            ("nuoc lam mat", "nuoc-lam-mat"), ("nuoc mat", "nuoc-lam-mat"),
            ("ac quy", "ac-quy"), ("acquy", "ac-quy"), ("binh dien", "ac-quy"), ("binh ac quy", "ac-quy"),
            ("dao lop", "dao-lop"),
            ("thay lop", "lop"), ("thay vo", "lop"), ("lop moi", "lop"), ("vo xe", "lop"),
            ("bugi", "bugi"), ("bu gi", "bugi"),
            ("nhong xich", "nhong-xich"), ("nhong sen dia", "nhong-xich"), ("nhong sen", "nhong-xich"), ("sen dia", "nhong-xich"),
            ("day curoa", "day-curoa"), ("curoa", "day-curoa"), ("day dai", "day-curoa"),
            ("loc nhot", "loc-dau"), ("loc dau", "loc-dau"), ("thay loc", "loc-dau"),
            ("thay nhot", "dau-may"), ("thay dau", "dau-may"), ("nhot may", "dau-may"), ("dau may", "dau-may"), ("dau nhot", "dau-may"), ("nhot", "dau-may"),
            ("bao duong", "bao-duong-dinh-ky")
        };
        private static readonly (string phrase, string tag)[] ExpensePhrases = new[]
        {
            ("bao hiem than vo", "bao-hiem-than-vo"), ("bao hiem vat chat", "bao-hiem-than-vo"),
            ("bao hiem", "bao-hiem"),
            ("dang kiem", "dang-kiem"),
            ("bao tri duong bo", "phi-duong-bo"), ("phi duong bo", "phi-duong-bo"),
            ("cau duong", "phi-cau-duong"), ("cao toc", "phi-cau-duong"), ("tram thu phi", "phi-cau-duong"), ("bot", "phi-cau-duong"), ("etc", "phi-cau-duong"), ("vetc", "phi-cau-duong"), ("epass", "phi-cau-duong"),
            ("gui xe", "gui-xe"), ("giu xe", "gui-xe"), ("bai xe", "gui-xe"), ("do xe", "gui-xe"), ("ve thang", "gui-xe"),
            ("rua xe", "rua-xe"),
            ("phat nguoi", "phat-nguoi"), ("nop phat", "phat-nguoi"), ("bi phat", "phat-nguoi"), ("tien phat", "phat-nguoi")
        };
        private static readonly string[] FuelPhrases = new[] { "do xang", "do dau", "do day binh", "xang", "sac dien", "sac pin" };
        private static readonly string[] ReminderQuestionPhrases = new[] { "bao gio", "khi nao", "den han", "sap den", "qua han", "lich bao duong", "can lam gi", "nhac lich" };
        private static readonly string[] SpendingQuestionPhrases = new[] { "bao nhieu tien", "tieu bao nhieu", "het bao nhieu", "chi bao nhieu", "ton bao nhieu", "tong chi", "chi phi thang", "chi phi nam", "tieu het bao nhieu" };

        /// <summary>
        /// Lower-cases and strips Vietnamese diacritics so that "Thay nhớt" and "thay nhot" compare equal.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            var decomposed = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }
            return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }
        private static decimal ParseNumber(string raw)
        {
            if (Regex.IsMatch(raw, @"^\d{1,3}(?:[.,]\d{3})+$"))
            {
                //thousands separators, i.e. 12.350 or 1,200,000
                return decimal.Parse(raw.Replace(".", "").Replace(",", ""), CultureInfo.InvariantCulture);
            }
            return decimal.Parse(raw.Replace(",", "."), CultureInfo.InvariantCulture);
        }
        private static bool ContainsPhrase(string text, string phrase)
        {
            return Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])");
        }
        private static string RemovePhrase(string text, string phrase)
        {
            return Regex.Replace(text, $@"(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])", " ");
        }
        private static int MatchVehicle(ref string text, List<Vehicle> vehicles)
        {
            if (vehicles.Count == 1)
            {
                return vehicles.First().Id;
            }
            var compactText = Regex.Replace(text, @"[^a-z0-9]", "");
            var scores = new Dictionary<int, int>();
            var matchedPhrases = new List<string>();
            foreach (var vehicle in vehicles)
            {
                int score = 0;
                var plate = Regex.Replace(Normalize(vehicle.LicensePlate), @"[^a-z0-9]", "");
                if (plate.Length >= 4 && compactText.Contains(plate))
                {
                    score += 4;
                }
                foreach (var tag in vehicle.Tags.Select(Normalize).Where(x => x.Length >= 2))
                {
                    if (ContainsPhrase(text, tag)) { score += 3; matchedPhrases.Add(tag); }
                }
                var model = Normalize(vehicle.Model);
                if (model.Length >= 2 && ContainsPhrase(text, model)) { score += 2; matchedPhrases.Add(model); }
                var make = Normalize(vehicle.Make);
                if (make.Length >= 2 && ContainsPhrase(text, make)) { score += 1; matchedPhrases.Add(make); }
                if (score > 0)
                {
                    scores[vehicle.Id] = score;
                }
            }
            if (!scores.Any())
            {
                //fall back to the kind of vehicle when exactly one vehicle is of that kind.
                List<Vehicle> candidates = new List<Vehicle>();
                if (ContainsPhrase(text, "xe may") || ContainsPhrase(text, "xe ga") || ContainsPhrase(text, "xe so"))
                {
                    candidates = vehicles.Where(x => x.VehicleCategory != VehicleCategory.Car).ToList();
                }
                else if (ContainsPhrase(text, "o to") || ContainsPhrase(text, "oto") || ContainsPhrase(text, "xe hoi") || ContainsPhrase(text, "xe hop"))
                {
                    candidates = vehicles.Where(x => x.VehicleCategory == VehicleCategory.Car).ToList();
                }
                return candidates.Count == 1 ? candidates.First().Id : 0;
            }
            foreach (var phrase in matchedPhrases.Distinct())
            {
                //vehicle names often contain digits (CX5, Wave 110) that must not be read as amounts.
                text = RemovePhrase(text, phrase);
            }
            var best = scores.Max(x => x.Value);
            var winners = scores.Where(x => x.Value == best).ToList();
            return winners.Count == 1 ? winners.First().Key : 0;
        }
        private static DateTime? ExtractDate(ref string text, DateTime today)
        {
            DateTime? result = null;
            if (ContainsPhrase(text, "hom kia")) { result = today.AddDays(-2); text = RemovePhrase(text, "hom kia"); }
            else if (ContainsPhrase(text, "hom qua")) { result = today.AddDays(-1); text = RemovePhrase(text, "hom qua"); }
            else if (ContainsPhrase(text, "hom nay")) { result = today; text = RemovePhrase(text, "hom nay"); }
            var patterns = new[]
            {
                @"(?:ngay\s+)?(?<![\d/.,-])(?<d>\d{1,2})[/-](?<m>\d{1,2})(?:[/-](?<y>\d{4}|\d{2}))?(?![\d/])",
                @"ngay\s+(?<d>\d{1,2})\s+thang\s+(?<m>\d{1,2})(?:\s+nam\s+(?<y>\d{4}))?"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (!match.Success)
                {
                    continue;
                }
                int day = int.Parse(match.Groups["d"].Value);
                int month = int.Parse(match.Groups["m"].Value);
                int year = today.Year;
                if (match.Groups["y"].Success)
                {
                    year = int.Parse(match.Groups["y"].Value);
                    if (year < 100) { year += 2000; }
                }
                if (month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                {
                    var parsed = new DateTime(year, month, day);
                    if (!match.Groups["y"].Success && parsed > today.AddDays(1))
                    {
                        //a day/month without a year that lies in the future means last year.
                        parsed = parsed.AddYears(-1);
                    }
                    result = parsed;
                    text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
                    break;
                }
            }
            return result;
        }
        private static decimal? ExtractFuelAmount(ref string text)
        {
            var match = Regex.Match(text, $@"(?<![a-z0-9.,])({Number})\s*(?:lit|l)(?![a-z0-9])");
            if (!match.Success)
            {
                return null;
            }
            text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
            return ParseNumber(match.Groups[1].Value);
        }
        private static int? ExtractMileage(ref string text)
        {
            var patterns = new[]
            {
                $@"(?:odo|odometer|cong to met|so km|dong ho)\s*(?:la|hien tai|:|=)?\s*(?<n>{Number})\s*(?<u>k|nghin|ngan|van)?(?:\s*(?:km|cay so|cay))?(?![a-z0-9])",
                $@"(?<![a-z0-9.,])(?<n>{Number})\s*(?<u>k|nghin|ngan|van)?\s*(?:km|cay so|cay)(?![a-z0-9])"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (!match.Success)
                {
                    continue;
                }
                var value = ParseNumber(match.Groups["n"].Value);
                switch (match.Groups["u"].Value)
                {
                    case "k": case "nghin": case "ngan": value *= 1000; break;
                    case "van": value *= 10000; break;
                }
                text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
                return (int)Math.Round(value);
            }
            return null;
        }
        private static decimal? ExtractCost(ref string text)
        {
            var amounts = new List<(int index, decimal value)>();
            string working = text;
            void Collect(string pattern, Func<Match, decimal> convert)
            {
                foreach (Match match in Regex.Matches(working, pattern))
                {
                    amounts.Add((match.Index, convert(match)));
                }
                //keep positions stable by overwriting with spaces.
                working = Regex.Replace(working, pattern, m => new string(' ', m.Length));
            }
            Collect($@"(?<![a-z0-9.,])(\d+)\s*(?:trieu|tr|cu)\s*ruoi(?![a-z0-9])", m => decimal.Parse(m.Groups[1].Value) * 1000000 + 500000);
            Collect($@"(?<![a-z0-9.,])(\d+)\s*(?:trieu|tr|cu)\s*(\d{{1,3}})(?![\d.,])", m =>
                decimal.Parse(m.Groups[1].Value) * 1000000 + decimal.Parse(m.Groups[2].Value) * (decimal)Math.Pow(10, 6 - m.Groups[2].Value.Length));
            Collect($@"(?<![a-z0-9.,])({Number})\s*(?:trieu|tr|cu)(?![a-z0-9])", m => ParseNumber(m.Groups[1].Value) * 1000000);
            Collect($@"(?<![a-z0-9.,])(\d+)\s*(?:nghin|ngan|k)\s*ruoi(?![a-z0-9])", m => decimal.Parse(m.Groups[1].Value) * 1000 + 500);
            Collect($@"(?<![a-z0-9.,])({Number})\s*(?:nghin|ngan|k|n)(?![a-z0-9])", m => ParseNumber(m.Groups[1].Value) * 1000);
            Collect($@"(?<![a-z0-9.,])({Number})\s*(?:dong|vnd|d)(?![a-z0-9])", m => ParseNumber(m.Groups[1].Value));
            Collect(@"(?<![a-z0-9.,])(\d{1,3}(?:[.,]\d{3})+)(?![a-z0-9.,])", m => ParseNumber(m.Groups[1].Value));
            Collect(@"(?<![a-z0-9.,])(\d{5,})(?![a-z0-9.,])", m => decimal.Parse(m.Groups[1].Value));
            if (!amounts.Any())
            {
                return null;
            }
            text = working;
            //an amount introduced as the total wins over itemised amounts.
            foreach (var amount in amounts.OrderBy(x => x.index))
            {
                var before = text.Substring(0, amount.index).TrimEnd();
                if (Regex.IsMatch(before, @"(?:tong cong|tong|tat ca|het|mat)(?:\s+la)?$"))
                {
                    return amount.value;
                }
            }
            return amounts.Sum(x => x.value);
        }
        public static ParsedMessage Parse(string message, List<Vehicle> vehicles, DateTime today)
        {
            var result = new ParsedMessage();
            var text = Normalize(message);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }
            result.MentionsLastMonth = ContainsPhrase(text, "thang truoc");
            result.MentionsThisYear = ContainsPhrase(text, "nam nay") || ContainsPhrase(text, "ca nam");
            bool isReminderQuestion = ReminderQuestionPhrases.Any(x => ContainsPhrase(text, x));
            bool isSpendingQuestion = SpendingQuestionPhrases.Any(x => ContainsPhrase(text, x));
            //items are looked up in the full message: a vehicle nickname such as "xe vo" may overlap a phrase such as "rua xe".
            var itemText = text;
            result.VehicleId = MatchVehicle(ref text, vehicles);
            result.Date = ExtractDate(ref text, today.Date);
            result.FuelAmount = ExtractFuelAmount(ref text);
            result.Mileage = ExtractMileage(ref text);
            result.Cost = ExtractCost(ref text);
            bool mentionsFuel = false;
            foreach (var phrase in FuelPhrases)
            {
                if (ContainsPhrase(itemText, phrase))
                {
                    mentionsFuel = true;
                    itemText = RemovePhrase(itemText, phrase);
                }
            }
            foreach (var (phrase, tag) in MaintenancePhrases)
            {
                if (ContainsPhrase(itemText, phrase))
                {
                    if (!result.MaintenanceTags.Contains(tag)) { result.MaintenanceTags.Add(tag); }
                    itemText = RemovePhrase(itemText, phrase);
                }
            }
            foreach (var (phrase, tag) in ExpensePhrases)
            {
                if (ContainsPhrase(itemText, phrase))
                {
                    if (!result.ExpenseTags.Contains(tag)) { result.ExpenseTags.Add(tag); }
                    itemText = RemovePhrase(itemText, phrase);
                }
            }
            if (isSpendingQuestion && !result.Cost.HasValue)
            {
                result.Intent = AiIntent.QuerySpending;
            }
            else if (isReminderQuestion && !result.Cost.HasValue)
            {
                result.Intent = AiIntent.QueryReminders;
            }
            else if (mentionsFuel || (result.FuelAmount.HasValue && !result.MaintenanceTags.Any()))
            {
                result.Intent = AiIntent.Fuel;
            }
            else if (result.MaintenanceTags.Any())
            {
                result.Intent = AiIntent.Service;
            }
            else if (result.ExpenseTags.Any() || result.Cost.HasValue)
            {
                result.Intent = AiIntent.Expense;
            }
            else if (result.Mileage.HasValue)
            {
                result.Intent = AiIntent.Odometer;
            }
            return result;
        }
    }
}
