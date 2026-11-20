using CarCareTracker.Logic;
using CarCareTracker.Models;
using Xunit;

namespace CarCareTracker.Tests
{
    public class FoodLabelRulesTests
    {
        private static NutrientRating Rating(FoodLabelAnalysis analysis, string nutrient) => analysis.Ratings.Single(x => x.Nutrient == nutrient);

        [Theory]
        [InlineData("fat", 3.0, FoodLight.Green)]
        [InlineData("fat", 3.1, FoodLight.Amber)]
        [InlineData("fat", 17.5, FoodLight.Amber)]
        [InlineData("fat", 17.6, FoodLight.Red)]
        [InlineData("saturates", 1.5, FoodLight.Green)]
        [InlineData("saturates", 5.1, FoodLight.Red)]
        [InlineData("sugars", 5.0, FoodLight.Green)]
        [InlineData("sugars", 22.6, FoodLight.Red)]
        [InlineData("salt", 0.3, FoodLight.Green)]
        [InlineData("salt", 1.5, FoodLight.Amber)]
        [InlineData("salt", 1.51, FoodLight.Red)]
        public void FoodThresholdsFollowTheFsaTable(string nutrient, double per100, FoodLight expected)
        {
            Assert.Equal(expected, FoodLabelRules.LightFor(FoodKind.Food, nutrient, (decimal)per100));
        }

        [Theory]
        [InlineData("sugars", 2.5, FoodLight.Green)]
        [InlineData("sugars", 11.25, FoodLight.Amber)]
        [InlineData("sugars", 11.3, FoodLight.Red)]
        [InlineData("salt", 0.76, FoodLight.Red)]
        [InlineData("fat", 8.8, FoodLight.Red)]
        public void DrinksUseHalfTheThresholds(string nutrient, double per100, FoodLight expected)
        {
            Assert.Equal(expected, FoodLabelRules.LightFor(FoodKind.Drink, nutrient, (decimal)per100));
        }

        [Fact]
        public void PerServingTableIsNormalisedTo100Grams()
        {
            var reading = new FoodLabelReading { Basis = NutrientBasis.PerServing, ServingSize = 30, Sugars = 9, Fat = 1.2m };
            var analysis = FoodLabelRules.Analyze(reading);
            Assert.Equal(30m, Rating(analysis, "sugars").Per100);
            Assert.Equal(FoodLight.Red, Rating(analysis, "sugars").Light);
            Assert.Equal(4m, Rating(analysis, "fat").Per100);
            //9 g of the WHO limit of 50 g of free sugars.
            Assert.Equal(18m, Rating(analysis, "sugars").PercentOfDailyLimit);
        }

        [Fact]
        public void PerServingWithoutServingSizeCannotBeRated()
        {
            var analysis = FoodLabelRules.Analyze(new FoodLabelReading { Basis = NutrientBasis.PerServing, Sugars = 9 });
            Assert.Equal(FoodLight.Unknown, Rating(analysis, "sugars").Light);
            Assert.Contains("The table is per serving but the serving size is missing", analysis.Issues);
        }

        [Fact]
        public void SaltComesFromSodiumWhenNotPrinted()
        {
            var reading = new FoodLabelReading { SodiumMg = 800 };
            Assert.Equal(2m, FoodLabelRules.SaltOf(reading));
            Assert.Equal(FoodLight.Red, Rating(FoodLabelRules.Analyze(reading), "salt").Light);
        }

        [Fact]
        public void LargePortionTurnsRedAboveThirtyPercentOfReferenceIntake()
        {
            //amber per 100 g (1.0 g salt), but a 200 g portion gives 2.0 g, over 30% of 6 g.
            var reading = new FoodLabelReading { Basis = NutrientBasis.Per100, ServingSize = 200, Salt = 1.0m };
            var salt = Rating(FoodLabelRules.Analyze(reading), "salt");
            Assert.Equal(FoodLight.Red, salt.Light);
            Assert.True(salt.RedByPortion);
            Assert.Equal(2m, salt.PerServing);
            Assert.Equal(40m, salt.PercentOfDailyLimit);
        }

        [Fact]
        public void PortionRuleDoesNotApplyToDrinksOrSmallPortions()
        {
            var small = FoodLabelRules.Analyze(new FoodLabelReading { ServingSize = 100, Salt = 1.0m });
            Assert.Equal(FoodLight.Amber, Rating(small, "salt").Light);
            var drink = FoodLabelRules.Analyze(new FoodLabelReading { Kind = FoodKind.Drink, ServingSize = 330, Sugars = 10 });
            Assert.Equal(FoodLight.Amber, Rating(drink, "sugars").Light);
        }

        [Fact]
        public void ImplausibleNumbersAreReported()
        {
            var reading = new FoodLabelReading { Fat = 2, SaturatedFat = 5, Carbohydrate = 10, Sugars = 12, Protein = 3, EnergyKcal = 500, Salt = 1.2m, SodiumMg = 1200 };
            var issues = FoodLabelRules.CheckNumbers(reading);
            Assert.Contains("Saturated fat is higher than total fat", issues);
            Assert.Contains("Sugars are higher than total carbohydrate", issues);
            Assert.Contains("Energy does not match fat, carbohydrate and protein", issues);
            Assert.Contains("Salt and sodium do not match (salt = sodium × 2.5)", issues);
        }

        [Fact]
        public void ConsistentLabelHasNoIssues()
        {
            //a typical biscuit: 9×20 + 4×65 + 4×7 = 468 kcal.
            var reading = new FoodLabelReading { EnergyKcal = 470, Fat = 20, SaturatedFat = 9, Carbohydrate = 65, Sugars = 25, Protein = 7, Salt = 0.8m, SodiumMg = 320 };
            Assert.Empty(FoodLabelRules.CheckNumbers(reading));
        }

        [Fact]
        public void NutrientsOverOneHundredGramsAreReported()
        {
            Assert.Contains("The nutrients add up to more than 100 g per 100 g", FoodLabelRules.CheckNumbers(new FoodLabelReading { Fat = 60, Carbohydrate = 50 }));
        }

        [Fact]
        public void AbsurdNumbersAreDropped()
        {
            var cleaned = FoodLabelRules.Clean(new FoodLabelReading { Fat = -3, Sugars = 5000, ServingSize = 0, Barcode = "893 4588 012345" });
            Assert.Null(cleaned.Fat);
            Assert.Null(cleaned.Sugars);
            Assert.Null(cleaned.ServingSize);
            Assert.Equal("8934588012345", cleaned.Barcode);
        }

        [Theory]
        [InlineData("Chất bảo quản (211, 202), chất điều vị (621)", new[] { "211", "202", "621" })]
        [InlineData("Ingredients: sugar, E330, E 211, INS 150d, E471", new[] { "330", "211", "150d", "471" })]
        [InlineData("chất điều chỉnh độ acid (INS 331(iii))", new[] { "331" })]
        [InlineData("Phẩm màu tổng hợp: Tartrazine, Sunset yellow FCF", new[] { "102", "110" })]
        [InlineData("Đường, bột ngọt, aspartame", new[] { "621", "951" })]
        public void AdditivesAreFoundByCodeAndName(string ingredients, string[] expected)
        {
            var codes = FoodLabelRules.FindAdditives(FoodLabelRules.NormalizeText(ingredients)).Select(x => x.Code).ToArray();
            Assert.Equal(expected, codes);
        }

        [Theory]
        [InlineData("Đường 12%, bột mì 100 g, sữa 2.5%")]
        [InlineData("Năng lượng 450 kcal, natri 330 mg")]
        [InlineData("Sản xuất năm 2026, lô 1600")]
        public void AmountsAreNotTakenForAdditives(string text)
        {
            Assert.Empty(FoodLabelRules.FindAdditives(FoodLabelRules.NormalizeText(text)));
        }

        [Fact]
        public void UnknownPrefixedCodeIsReportedAsUnknown()
        {
            var finding = Assert.Single(FoodLabelRules.FindAdditives(FoodLabelRules.NormalizeText("E999")));
            Assert.False(finding.Known);
            Assert.Equal("999", finding.Code);
        }

        [Fact]
        public void EveryCautionNoteNamesItsSource()
        {
            foreach (var additive in FoodReference.Additives.Values)
            {
                Assert.True(FoodReference.Classes.ContainsKey(additive.Class), additive.Code);
                foreach (var note in additive.Notes)
                {
                    Assert.False(string.IsNullOrWhiteSpace(note.SourceName), additive.Code);
                    Assert.StartsWith("https://", note.SourceUrl);
                    Assert.False(string.IsNullOrWhiteSpace(note.TextVi));
                }
            }
        }

        [Fact]
        public void SouthamptonColoursCarryTheEuWarning()
        {
            foreach (var code in new[] { "102", "104", "110", "122", "124", "129" })
            {
                Assert.Contains(FoodReference.Additives[code].Notes, x => x.Level == FoodNoteLevel.Caution && x.SourceUrl.Contains("2008/1333"));
            }
            //permitted, unremarkable additives get no warning at all.
            Assert.Empty(FoodReference.Additives["330"].Notes);
        }

        [Fact]
        public void AllergensAreFoundAndMayContainIsSeparated()
        {
            var text = FoodLabelRules.NormalizeText("Bột mì, đường, dầu cọ, sữa bột, trứng. Có thể chứa đậu phộng và mè.");
            var allergens = FoodLabelRules.FindAllergens(text, new List<AdditiveFinding>());
            Assert.False(allergens.Single(x => x.Group == "gluten").MayContain);
            Assert.False(allergens.Single(x => x.Group == "milk").MayContain);
            Assert.False(allergens.Single(x => x.Group == "eggs").MayContain);
            Assert.True(allergens.Single(x => x.Group == "peanuts").MayContain);
            Assert.True(allergens.Single(x => x.Group == "sesame").MayContain);
            Assert.DoesNotContain(allergens, x => x.Group == "fish");
        }

        [Theory]
        [InlineData("Nước cốt dừa, sữa dừa, đường")]
        [InlineData("Cocoa butter, sugar, soy lecithin")]
        [InlineData("Sữa đậu nành nguyên chất")]
        public void PlantMilksAndCocoaButterAreNotMilk(string ingredients)
        {
            var allergens = FoodLabelRules.FindAllergens(FoodLabelRules.NormalizeText(ingredients), new List<AdditiveFinding>());
            Assert.DoesNotContain(allergens, x => x.Group == "milk");
        }

        [Fact]
        public void NegatedAllergenIsIgnoredAndWordBoundariesHold()
        {
            var allergens = FoodLabelRules.FindAllergens(FoodLabelRules.NormalizeText("Không chứa gluten. Các loại rau, maltodextrin, cà chua."), new List<AdditiveFinding>());
            Assert.Empty(allergens);
        }

        [Fact]
        public void SulphiteCodeCountsAsAllergen()
        {
            var text = FoodLabelRules.NormalizeText("Nho khô, chất bảo quản (220)");
            var additives = FoodLabelRules.FindAdditives(text);
            var allergen = Assert.Single(FoodLabelRules.FindAllergens(text, additives));
            Assert.Equal("sulphites", allergen.Group);
        }

        [Fact]
        public void ProfileMarksOnlyTheUsersOwnAllergens()
        {
            var analysis = FoodLabelRules.Analyze(new FoodLabelReading { Ingredients = "Bột mì, sữa, trứng" }, new[] { "milk" });
            Assert.True(analysis.Allergens.Single(x => x.Group == "milk").InProfile);
            Assert.False(analysis.Allergens.Single(x => x.Group == "eggs").InProfile);
        }

        [Fact]
        public void IngredientFlagsComeFromSourcedNotes()
        {
            var drink = FoodLabelRules.Analyze(new FoodLabelReading
            {
                Kind = FoodKind.Drink,
                Ingredients = "Nước, đường, chất bảo quản natri benzoat (211), vitamin C (300), chất tạo ngọt (950, 955), caffeine"
            });
            Assert.Contains(FoodReference.SweetenerNote, drink.Flags);
            Assert.Contains(FoodReference.CaffeineNote, drink.Flags);
            Assert.Contains(FoodReference.BenzoateAscorbicNote, drink.Flags);

            var biscuit = FoodLabelRules.Analyze(new FoodLabelReading { Ingredients = "Bột mì, dầu thực vật hydro hóa một phần", TransFat = 0.5m, Fat = 20 });
            Assert.Contains(FoodReference.PartiallyHydrogenatedNote, biscuit.Flags);
            Assert.Contains(FoodReference.TransFatNote, biscuit.Flags);

            //sugar alcohols are not "non-sugar sweeteners" in the WHO guideline.
            var gum = FoodLabelRules.Analyze(new FoodLabelReading { Ingredients = "Xylitol, sorbitol" });
            Assert.DoesNotContain(FoodReference.SweetenerNote, gum.Flags);
        }

        [Fact]
        public void EmptyReadingSaysThereIsNothingToAnalyse()
        {
            var analysis = FoodLabelRules.Analyze(new FoodLabelReading());
            Assert.False(analysis.HasNutrition);
            Assert.Contains("Nothing to analyse: no nutrition table and no ingredient list", analysis.Issues);
            Assert.Equal(FoodReference.Version, analysis.ReferenceVersion);
        }

        [Fact]
        public void ProfileKeysAreLimitedToKnownGroups()
        {
            Assert.Equal(new[] { "milk", "peanuts" }, FoodLabelRules.CleanAllergenKeys(new[] { "milk", "peanuts", "milk", "<script>" }));
        }
    }
}
