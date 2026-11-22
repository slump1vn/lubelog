using CarCareTracker.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CarCareTracker.Logic
{
    /// <summary>
    /// Deterministic analysis of a checked food label: per-100 g normalisation, UK FSA traffic lights, WHO daily
    /// limits, additives by INS/E number, allergens and ingredient flags. The AI only reads the label; every
    /// judgement is made here, from FoodReference, so the same label always gives the same result.
    /// </summary>
    public static class FoodLabelRules
    {
        public static readonly string[] RatedNutrients = { "fat", "saturates", "sugars", "salt" };
        /// <summary>Sweeteners covered by the WHO 2023 non-sugar sweetener guideline (sugar alcohols are not).</summary>
        private static readonly HashSet<string> NonSugarSweeteners = new HashSet<string> { "950", "951", "952", "954", "955", "960", "961", "962", "969" };

        /// <summary>Cleans a reading: negative or absurd numbers become null, text is trimmed and capped.</summary>
        public static FoodLabelReading Clean(FoodLabelReading reading)
        {
            decimal? Grams(decimal? value, decimal max) => value.HasValue && value.Value >= 0 && value.Value <= max ? Math.Round(value.Value, 2) : null;
            string Text(string? value, int max)
            {
                var cleaned = Regex.Replace(value ?? string.Empty, @"[ \t]+", " ").Trim();
                return cleaned.Length > max ? cleaned.Substring(0, max) : cleaned;
            }
            var serving = Grams(reading.ServingSize, 5000);
            return new FoodLabelReading
            {
                ProductName = Text(reading.ProductName, 120),
                Brand = Text(reading.Brand, 80),
                Barcode = new string((reading.Barcode ?? string.Empty).Where(char.IsDigit).Take(14).ToArray()),
                Kind = Enum.IsDefined(reading.Kind) ? reading.Kind : FoodKind.Food,
                Basis = Enum.IsDefined(reading.Basis) ? reading.Basis : NutrientBasis.Per100,
                ServingSize = serving == 0 ? null : serving,
                NetQuantity = Grams(reading.NetQuantity, 100000),
                EnergyKcal = Grams(reading.EnergyKcal, 5000),
                Fat = Grams(reading.Fat, 1000),
                SaturatedFat = Grams(reading.SaturatedFat, 1000),
                TransFat = Grams(reading.TransFat, 1000),
                Carbohydrate = Grams(reading.Carbohydrate, 1000),
                Sugars = Grams(reading.Sugars, 1000),
                Fibre = Grams(reading.Fibre, 1000),
                Protein = Grams(reading.Protein, 1000),
                Salt = Grams(reading.Salt, 1000),
                SodiumMg = Grams(reading.SodiumMg, 1000000),
                Ingredients = Text(reading.Ingredients, 4000),
                Uncertain = (reading.Uncertain ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().Take(30).ToList()
            };
        }

        /// <summary>Salt in grams: as printed, or from sodium (salt = sodium × 2.5).</summary>
        public static decimal? SaltOf(FoodLabelReading reading)
        {
            if (reading.Salt.HasValue)
            {
                return reading.Salt;
            }
            return reading.SodiumMg.HasValue ? Math.Round(reading.SodiumMg.Value * 2.5m / 1000m, 2) : null;
        }
        public static decimal? Per100(FoodLabelReading reading, decimal? value)
        {
            if (!value.HasValue)
            {
                return null;
            }
            if (reading.Basis == NutrientBasis.Per100)
            {
                return value;
            }
            return reading.ServingSize.HasValue && reading.ServingSize.Value > 0 ? Math.Round(value.Value * 100m / reading.ServingSize.Value, 2) : null;
        }
        public static decimal? PerServing(FoodLabelReading reading, decimal? value)
        {
            if (!value.HasValue)
            {
                return null;
            }
            if (reading.Basis == NutrientBasis.PerServing)
            {
                return value;
            }
            return reading.ServingSize.HasValue && reading.ServingSize.Value > 0 ? Math.Round(value.Value * reading.ServingSize.Value / 100m, 2) : null;
        }
        private static decimal? ValueOf(FoodLabelReading reading, string nutrient) => nutrient switch
        {
            "fat" => reading.Fat,
            "saturates" => reading.SaturatedFat,
            "sugars" => reading.Sugars,
            "salt" => SaltOf(reading),
            _ => null
        };
        public static FoodLight LightFor(FoodKind kind, string nutrient, decimal? per100)
        {
            if (!per100.HasValue)
            {
                return FoodLight.Unknown;
            }
            var threshold = (kind == FoodKind.Drink ? FoodReference.DrinkThresholds : FoodReference.FoodThresholds)[nutrient];
            if (per100.Value <= threshold.Low)
            {
                return FoodLight.Green;
            }
            return per100.Value > threshold.High ? FoodLight.Red : FoodLight.Amber;
        }

        public static FoodLabelAnalysis Analyze(FoodLabelReading input, IEnumerable<string>? profileAllergens = null)
        {
            var reading = Clean(input);
            var analysis = new FoodLabelAnalysis { ReferenceVersion = FoodReference.Version };
            foreach (var nutrient in RatedNutrients)
            {
                var value = ValueOf(reading, nutrient);
                var per100 = Per100(reading, value);
                var perServing = PerServing(reading, value);
                var limit = FoodReference.DailyLimits[nutrient];
                var rating = new NutrientRating
                {
                    Nutrient = nutrient,
                    Per100 = per100,
                    PerServing = perServing,
                    Light = LightFor(reading.Kind, nutrient, per100),
                    DailyLimit = limit,
                    PercentOfDailyLimit = perServing.HasValue ? Math.Round(perServing.Value * 100m / limit, 0) : null
                };
                bool bigPortion = reading.Kind == FoodKind.Food && reading.ServingSize.HasValue && reading.ServingSize.Value > FoodReference.PortionRuleMinimum;
                if (bigPortion && perServing.HasValue && perServing.Value > FoodReference.PortionRedAbove[nutrient] && rating.Light != FoodLight.Red)
                {
                    rating.Light = FoodLight.Red;
                    rating.RedByPortion = true;
                }
                analysis.Ratings.Add(rating);
            }
            analysis.HasNutrition = analysis.Ratings.Any(x => x.Per100.HasValue);
            analysis.EnergyKcalPer100 = Per100(reading, reading.EnergyKcal);
            analysis.TransFatPer100 = Per100(reading, reading.TransFat);
            analysis.Issues = CheckNumbers(reading);

            var text = NormalizeText(reading.Ingredients);
            analysis.Additives = FindAdditives(text);
            analysis.Allergens = FindAllergens(text, analysis.Additives);
            var profile = new HashSet<string>(profileAllergens ?? Enumerable.Empty<string>());
            foreach (var allergen in analysis.Allergens)
            {
                allergen.InProfile = profile.Contains(allergen.Group);
            }

            if (ContainsAny(text, FoodReference.PartiallyHydrogenated))
            {
                analysis.Flags.Add(FoodReference.PartiallyHydrogenatedNote);
            }
            if (reading.TransFat.HasValue && reading.TransFat.Value > 0)
            {
                analysis.Flags.Add(FoodReference.TransFatNote);
            }
            if (analysis.Additives.Any(x => NonSugarSweeteners.Contains(x.Code)))
            {
                analysis.Flags.Add(FoodReference.SweetenerNote);
            }
            if (ContainsAny(text, FoodReference.Caffeine))
            {
                analysis.Flags.Add(FoodReference.CaffeineNote);
            }
            if (reading.Kind == FoodKind.Drink && analysis.Additives.Any(x => x.Code == "211") && analysis.Additives.Any(x => x.Code == "300"))
            {
                analysis.Flags.Add(FoodReference.BenzoateAscorbicNote);
            }
            if (!analysis.HasNutrition && string.IsNullOrWhiteSpace(reading.Ingredients))
            {
                analysis.Issues.Add("Nothing to analyse: no nutrition table and no ingredient list");
            }
            return analysis;
        }

        /// <summary>Plausibility checks that catch the usual misreadings (g and mg, a lost decimal comma).</summary>
        public static List<string> CheckNumbers(FoodLabelReading reading)
        {
            var issues = new List<string>();
            bool anyNutrient = new[] { reading.EnergyKcal, reading.Fat, reading.SaturatedFat, reading.Carbohydrate, reading.Sugars, reading.Protein, reading.Salt, reading.SodiumMg }.Any(x => x.HasValue);
            if (reading.Basis == NutrientBasis.PerServing && !reading.ServingSize.HasValue && anyNutrient)
            {
                issues.Add("The table is per serving but the serving size is missing");
            }
            if (reading.Fat.HasValue && reading.SaturatedFat.HasValue && reading.SaturatedFat.Value > reading.Fat.Value + 0.05m)
            {
                issues.Add("Saturated fat is higher than total fat");
            }
            if (reading.Fat.HasValue && reading.TransFat.HasValue && reading.TransFat.Value > reading.Fat.Value + 0.05m)
            {
                issues.Add("Trans fat is higher than total fat");
            }
            if (reading.Carbohydrate.HasValue && reading.Sugars.HasValue && reading.Sugars.Value > reading.Carbohydrate.Value + 0.05m)
            {
                issues.Add("Sugars are higher than total carbohydrate");
            }
            var fat100 = Per100(reading, reading.Fat) ?? 0;
            var carb100 = Per100(reading, reading.Carbohydrate) ?? 0;
            var protein100 = Per100(reading, reading.Protein) ?? 0;
            var fibre100 = Per100(reading, reading.Fibre) ?? 0;
            var salt100 = Per100(reading, SaltOf(reading)) ?? 0;
            if (fat100 + carb100 + protein100 + fibre100 + salt100 > 101m)
            {
                issues.Add("The nutrients add up to more than 100 g per 100 g");
            }
            if (reading.EnergyKcal.HasValue && reading.Fat.HasValue && reading.Carbohydrate.HasValue && reading.Protein.HasValue)
            {
                var computed = 9m * reading.Fat.Value + 4m * reading.Carbohydrate.Value + 4m * reading.Protein.Value + 2m * (reading.Fibre ?? 0);
                var difference = Math.Abs(computed - reading.EnergyKcal.Value);
                if (difference > Math.Max(40m, reading.EnergyKcal.Value * 0.25m))
                {
                    issues.Add("Energy does not match fat, carbohydrate and protein");
                }
            }
            if (reading.Salt.HasValue && reading.SodiumMg.HasValue && reading.Salt.Value > 0)
            {
                var fromSodium = reading.SodiumMg.Value * 2.5m / 1000m;
                if (Math.Abs(fromSodium - reading.Salt.Value) > Math.Max(0.1m, reading.Salt.Value * 0.2m))
                {
                    issues.Add("Salt and sodium do not match (salt = sodium × 2.5)");
                }
            }
            if (reading.ServingSize.HasValue && reading.NetQuantity.HasValue && reading.NetQuantity.Value > 0 && reading.ServingSize.Value > reading.NetQuantity.Value)
            {
                issues.Add("The serving is larger than the pack");
            }
            return issues;
        }

        /// <summary>Lower case, composed Unicode, one space between words, roman-numeral suffixes joined to the code.</summary>
        public static string NormalizeText(string? value)
        {
            var text = (value ?? string.Empty).Normalize(NormalizationForm.FormC).ToLowerInvariant();
            text = text.Replace(' ', ' ').Replace('–', '-').Replace('—', '-');
            text = Regex.Replace(text, @"\s+", " ");
            //"331(iii)" and "331 (iii)" read as "331iii".
            text = Regex.Replace(text, @"(\d{3,4}[a-f]?)\s*\((i{1,3}|iv|vi|v)\)", "$1$2");
            return text.Trim();
        }
        private const string NotLetterBefore = @"(?<![\p{L}\p{N}])";
        private const string NotLetterAfter = @"(?![\p{L}\p{N}])";
        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            return words.Any(word => Regex.IsMatch(text, NotLetterBefore + Regex.Escape(word) + NotLetterAfter));
        }
        /// <summary>The reference key for a code: "150d" when the table has the lettered code, else the number.</summary>
        public static (string Code, bool Known) ResolveCode(string number, string letter)
        {
            if (!string.IsNullOrEmpty(letter) && FoodReference.Additives.ContainsKey(number + letter))
            {
                return (number + letter, true);
            }
            if (FoodReference.Additives.ContainsKey(number))
            {
                return (number, true);
            }
            return (number + letter, false);
        }
        private static readonly Regex PrefixedCode = new Regex(NotLetterBefore + @"(?:e|ins)\s*[-.]?\s*(\d{3,4})([a-f](?![a-z]))?(?:i{1,3}|iv|vi|v)?" + NotLetterAfter, RegexOptions.Compiled);
        //a bare number is only taken as an additive when it is in the reference table and is not an amount.
        private static readonly Regex BareCode = new Regex(@"(?<![\p{L}\p{N}.,%])(\d{3,4})([a-f](?![a-z]))?(?:i{1,3}|iv|vi|v)?(?![\p{L}\p{N}%]|[.,]\d|\s*(?:%|g\b|mg\b|mcg\b|µg\b|kg\b|ml\b|l\b|kcal\b|kj\b|iu\b))", RegexOptions.Compiled);

        public static List<AdditiveFinding> FindAdditives(string normalizedText)
        {
            var found = new List<(int Position, string Code, bool Known)>();
            foreach (Match match in PrefixedCode.Matches(normalizedText))
            {
                var (code, known) = ResolveCode(match.Groups[1].Value, match.Groups[2].Value);
                found.Add((match.Index, code, known));
            }
            foreach (Match match in BareCode.Matches(normalizedText))
            {
                var (code, known) = ResolveCode(match.Groups[1].Value, match.Groups[2].Value);
                if (known)
                {
                    found.Add((match.Index, code, true));
                }
            }
            foreach (var (word, code) in FoodReference.AdditiveNames)
            {
                var match = Regex.Match(normalizedText, NotLetterBefore + Regex.Escape(word) + NotLetterAfter);
                if (match.Success)
                {
                    found.Add((match.Index, code, true));
                }
            }
            return found
                .OrderBy(x => x.Position)
                .GroupBy(x => x.Code)
                .Select(group =>
                {
                    var first = group.First();
                    if (first.Known && FoodReference.Additives.TryGetValue(first.Code, out var additive))
                    {
                        return new AdditiveFinding { Code = additive.Code, NameVi = additive.Vi, NameEn = additive.En, Class = additive.Class, Known = true, Notes = additive.Notes.ToList() };
                    }
                    return new AdditiveFinding { Code = first.Code, Known = false };
                })
                .ToList();
        }

        /// <summary>Phrases that contain an allergen word but are not that allergen (coconut milk is not milk).</summary>
        private static readonly IReadOnlyDictionary<string, string[]> AllergenExclusions = new Dictionary<string, string[]>
        {
            ["milk"] = new[] { "sữa đậu nành", "sữa dừa", "sữa hạnh nhân", "sữa yến mạch", "sữa gạo", "sữa ong chúa", "sữa hạt", "coconut milk", "soy milk", "soya milk", "almond milk", "oat milk", "rice milk", "cocoa butter", "bơ ca cao", "bơ cacao", "cream of tartar" },
            ["gluten"] = new[] { "không chứa gluten", "gluten free", "gluten-free" }
        };
        private static readonly string[] Negations = { "không chứa", "không có", "free from", "không dùng" };

        public static List<AllergenFinding> FindAllergens(string normalizedText, List<AdditiveFinding> additives)
        {
            //"may contain" statements run to the end of their sentence.
            var precautionary = new List<(int Start, int End)>();
            foreach (var phrase in FoodReference.MayContainPhrases)
            {
                foreach (Match match in Regex.Matches(normalizedText, Regex.Escape(phrase)))
                {
                    int end = normalizedText.IndexOfAny(new[] { '.', ';', '\n' }, match.Index);
                    precautionary.Add((match.Index, end < 0 ? normalizedText.Length : end));
                }
            }
            var results = new List<AllergenFinding>();
            foreach (var group in FoodReference.Allergens)
            {
                var text = normalizedText;
                if (AllergenExclusions.TryGetValue(group.Key, out var exclusions))
                {
                    foreach (var exclusion in exclusions)
                    {
                        text = text.Replace(exclusion, new string(' ', exclusion.Length));
                    }
                }
                AllergenFinding? finding = null;
                foreach (var word in group.Words)
                {
                    foreach (Match match in Regex.Matches(text, NotLetterBefore + Regex.Escape(word) + NotLetterAfter))
                    {
                        var before = text.Substring(Math.Max(0, match.Index - 14), Math.Min(14, match.Index));
                        if (Negations.Any(before.Contains))
                        {
                            continue;
                        }
                        bool mayContain = precautionary.Any(x => match.Index > x.Start && match.Index < x.End);
                        if (finding == null)
                        {
                            finding = new AllergenFinding { Group = group.Key, Matched = match.Value, MayContain = mayContain };
                        }
                        else if (finding.MayContain && !mayContain)
                        {
                            //a plain mention outweighs a "may contain" one.
                            finding.MayContain = false;
                            finding.Matched = match.Value;
                        }
                    }
                }
                if (finding == null)
                {
                    var code = additives.FirstOrDefault(x => FoodReference.AllergenCodes.TryGetValue(x.Code, out var allergen) && allergen == group.Key);
                    if (code != null)
                    {
                        finding = new AllergenFinding { Group = group.Key, Matched = $"INS {code.Code}" };
                    }
                }
                if (finding != null)
                {
                    results.Add(finding);
                }
            }
            return results;
        }

        /// <summary>Allergen keys a user may store in their profile; anything else is dropped.</summary>
        public static List<string> CleanAllergenKeys(IEnumerable<string>? keys)
        {
            var valid = FoodReference.Allergens.Select(x => x.Key).ToHashSet();
            return (keys ?? Enumerable.Empty<string>()).Where(valid.Contains).Distinct().ToList();
        }
    }
}
