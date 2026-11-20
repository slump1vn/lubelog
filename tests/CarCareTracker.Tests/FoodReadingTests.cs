using CarCareTracker.Controllers;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using System.Text.Json;
using Xunit;

namespace CarCareTracker.Tests
{
    public class FoodReadingTests
    {
        private static FoodLogic.ModelLabel Label(string kind) => new FoodLogic.ModelLabel { kind = kind };

        [Fact]
        public void NutritionComesFromThePhotoWithTheTableAndTextIsJoined()
        {
            var front = Label("food");
            front.product_name = "Bánh quy bơ";
            front.net_quantity = 300;
            var table = Label("food");
            table.basis = "per_serving";
            table.serving_size = 30;
            table.energy_kj = 628;
            table.fat = 6;
            table.sugars = 7.5m;
            table.sodium_mg = 90;
            table.uncertain = new List<string> { "sugars", "nonsense" };
            var back = Label("food");
            back.ingredients = "Bột mì, đường, bơ sữa (10%). Có thể chứa đậu phộng.";
            var reading = FoodLogic.Merge(new List<FoodLogic.ModelLabel> { front, table, back });
            Assert.Equal("Bánh quy bơ", reading.ProductName);
            Assert.Equal(NutrientBasis.PerServing, reading.Basis);
            Assert.Equal(30m, reading.ServingSize);
            Assert.Equal(300m, reading.NetQuantity);
            //628 kJ / 4.184 = 150 kcal.
            Assert.Equal(150m, reading.EnergyKcal);
            Assert.Equal(6m, reading.Fat);
            Assert.Null(reading.SaturatedFat);
            Assert.Equal(90m, reading.SodiumMg);
            Assert.Contains("Có thể chứa đậu phộng", reading.Ingredients);
            Assert.Equal(new[] { nameof(FoodLabelReading.Sugars) }, reading.Uncertain);
        }

        [Fact]
        public void DrinkWinsAndNonLabelsAreIgnored()
        {
            var drink = Label("drink");
            drink.sugars = 10;
            var reading = FoodLogic.Merge(new List<FoodLogic.ModelLabel> { Label("not_food_label"), drink });
            Assert.Equal(FoodKind.Drink, reading.Kind);
            Assert.Equal(10m, reading.Sugars);
            Assert.Equal(string.Empty, FoodLogic.Merge(new List<FoodLogic.ModelLabel> { Label("not_food_label") }).ProductName);
        }

        [Fact]
        public void SchemaRequiresEveryField()
        {
            var schema = FoodLogic.BuildSchema();
            var properties = schema["properties"]!.AsObject().Select(x => x.Key).OrderBy(x => x);
            var required = schema["required"]!.AsArray().Select(x => x!.GetValue<string>()).OrderBy(x => x);
            Assert.Equal(properties, required);
            //every field the model returns maps onto the C# class.
            var modelFields = typeof(FoodLogic.ModelLabel).GetProperties().Select(x => x.Name).OrderBy(x => x);
            Assert.Equal(properties, modelFields);
        }

        [Fact]
        public void EnumsTravelAsNames()
        {
            var json = JsonSerializer.Serialize(new FoodLabelReading { Kind = FoodKind.Drink, Basis = NutrientBasis.PerServing });
            Assert.Contains("\"Kind\":\"Drink\"", json);
            Assert.Contains("\"Basis\":\"PerServing\"", json);
            var back = JsonSerializer.Deserialize<FoodLabelReading>(json)!;
            Assert.Equal(FoodKind.Drink, back.Kind);
        }

        [Fact]
        public void OnlyJpegAndPngContentIsAccepted()
        {
            Assert.True(FoodImages.IsJpegOrPng(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }));
            Assert.True(FoodImages.IsJpegOrPng(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
            Assert.False(FoodImages.IsJpegOrPng(System.Text.Encoding.ASCII.GetBytes("<svg onload=alert(1)>")));
        }

        [Fact]
        public void TransactionTextShowsVietnamTimeAndAmount()
        {
            var text = FoodLogic.TransactionText(new BankTransaction { Amount = 245000, Content = "BACH HOA XANH", OccurredAtUtc = new DateTime(2026, 11, 20, 3, 15, 0, DateTimeKind.Utc) });
            Assert.StartsWith("20/11/2026 10:15", text);
            Assert.Contains("BACH HOA XANH", text);
        }
    }
}
