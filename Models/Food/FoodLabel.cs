using System.Text.Json.Serialization;

namespace CarCareTracker.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<FoodKind>))]
    public enum FoodKind
    {
        Food = 0,
        Drink = 1
    }
    /// <summary>Whether the label's nutrition table is per 100 g/ml or per serving.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<NutrientBasis>))]
    public enum NutrientBasis
    {
        Per100 = 0,
        PerServing = 1
    }
    [JsonConverter(typeof(JsonStringEnumConverter<FoodLight>))]
    public enum FoodLight
    {
        Unknown = 0,
        Green = 1,
        Amber = 2,
        Red = 3
    }
    /// <summary>How much a note matters: plain information, or something worth a second look.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FoodNoteLevel>))]
    public enum FoodNoteLevel
    {
        Info = 0,
        Caution = 1
    }
    /// <summary>
    /// What was read from a food label. The home AI fills it in, the user checks and corrects it, and only
    /// then is it analysed. Weights are in grams, sodium in milligrams, energy in kcal; null means not on the label.
    /// </summary>
    public class FoodLabelReading
    {
        public string ProductName { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public FoodKind Kind { get; set; }
        public NutrientBasis Basis { get; set; }
        /// <summary>One serving in g (food) or ml (drink).</summary>
        public decimal? ServingSize { get; set; }
        /// <summary>Net quantity of the pack in g or ml.</summary>
        public decimal? NetQuantity { get; set; }
        public decimal? EnergyKcal { get; set; }
        public decimal? Fat { get; set; }
        public decimal? SaturatedFat { get; set; }
        public decimal? TransFat { get; set; }
        public decimal? Carbohydrate { get; set; }
        public decimal? Sugars { get; set; }
        public decimal? Fibre { get; set; }
        public decimal? Protein { get; set; }
        public decimal? Salt { get; set; }
        public decimal? SodiumMg { get; set; }
        /// <summary>The ingredient list as printed, including any "may contain" statement.</summary>
        public string Ingredients { get; set; } = string.Empty;
        /// <summary>Field names the AI was not sure about, shown highlighted on the check screen.</summary>
        public List<string> Uncertain { get; set; } = new List<string>();
    }
    /// <summary>One traffic light: fat, saturates, sugars or salt.</summary>
    public class NutrientRating
    {
        /// <summary>"fat", "saturates", "sugars" or "salt".</summary>
        public string Nutrient { get; set; } = string.Empty;
        public decimal? Per100 { get; set; }
        public decimal? PerServing { get; set; }
        public FoodLight Light { get; set; }
        /// <summary>True when the light is red because of the portion size, not the amount per 100 g.</summary>
        public bool RedByPortion { get; set; }
        /// <summary>One serving as a share of the WHO daily upper limit, in percent; null without a serving size.</summary>
        public decimal? PercentOfDailyLimit { get; set; }
        /// <summary>The WHO daily upper limit used, in grams.</summary>
        public decimal DailyLimit { get; set; }
    }
    /// <summary>A sourced remark about an additive or ingredient.</summary>
    public class FoodNote
    {
        public FoodNoteLevel Level { get; set; }
        public string TextVi { get; set; } = string.Empty;
        public string TextEn { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
    }
    public class AdditiveFinding
    {
        /// <summary>INS number as normalised, e.g. "211", "150d".</summary>
        public string Code { get; set; } = string.Empty;
        public string NameVi { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        /// <summary>Functional class key, e.g. "preservative".</summary>
        public string Class { get; set; } = string.Empty;
        /// <summary>False when the code is not in the reference table.</summary>
        public bool Known { get; set; }
        public List<FoodNote> Notes { get; set; } = new List<FoodNote>();
    }
    public class AllergenFinding
    {
        /// <summary>Allergen group key, e.g. "milk", "peanuts".</summary>
        public string Group { get; set; } = string.Empty;
        /// <summary>The words on the label that matched.</summary>
        public string Matched { get; set; } = string.Empty;
        /// <summary>Only in a "may contain" statement.</summary>
        public bool MayContain { get; set; }
        /// <summary>The signed-in user listed this group in their own allergy profile. Never stored with a scan.</summary>
        public bool InProfile { get; set; }
    }
    /// <summary>The result of analysing a checked reading. Computed by FoodLabelRules, never by the AI.</summary>
    public class FoodLabelAnalysis
    {
        public string ReferenceVersion { get; set; } = string.Empty;
        public bool HasNutrition { get; set; }
        public decimal? EnergyKcalPer100 { get; set; }
        public decimal? TransFatPer100 { get; set; }
        public List<NutrientRating> Ratings { get; set; } = new List<NutrientRating>();
        public List<AdditiveFinding> Additives { get; set; } = new List<AdditiveFinding>();
        public List<AllergenFinding> Allergens { get; set; } = new List<AllergenFinding>();
        /// <summary>Ingredient-level remarks: partially hydrogenated fat, sweeteners, caffeine...</summary>
        public List<FoodNote> Flags { get; set; } = new List<FoodNote>();
        /// <summary>Numbers that do not add up; message keys to translate.</summary>
        public List<string> Issues { get; set; } = new List<string>();
        public int RedCount => Ratings.Count(x => x.Light == FoodLight.Red);
        public int AmberCount => Ratings.Count(x => x.Light == FoodLight.Amber);
        public int GreenCount => Ratings.Count(x => x.Light == FoodLight.Green);
    }
    /// <summary>A saved scan, visible to the whole household. Holds no allergy profile data.</summary>
    public class FoodScan
    {
        public int Id { get; set; }
        public int HouseholdId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public FoodLabelReading Reading { get; set; } = new FoodLabelReading();
        public FoodLabelAnalysis Analysis { get; set; } = new FoodLabelAnalysis();
        /// <summary>Joint-account transaction this purchase was paid with; 0 when not linked.</summary>
        public int TransactionId { get; set; }
        /// <summary>Short description of the linked transaction, kept in case it is deleted later.</summary>
        public string TransactionText { get; set; } = string.Empty;
    }
    /// <summary>
    /// A user's own allergy profile. Health data: opt-in, visible to that user only, deletable, and never sent
    /// to the AI (matching against the label is done in code).
    /// </summary>
    public class FoodProfile
    {
        /// <summary>The user id.</summary>
        public int Id { get; set; }
        /// <summary>Allergen group keys from FoodReference.Allergens.</summary>
        public List<string> Allergens { get; set; } = new List<string>();
        public DateTime UpdatedAtUtc { get; set; }
    }
    public class FoodAnalyzeRequest
    {
        public FoodLabelReading Reading { get; set; } = new FoodLabelReading();
        /// <summary>Save the scan to the household history.</summary>
        public bool Save { get; set; }
    }
    public class FoodLinkRequest
    {
        public int ScanId { get; set; }
        /// <summary>0 removes the link.</summary>
        public int TransactionId { get; set; }
    }
    public class FoodScanDelete
    {
        public int Id { get; set; }
    }
    public class FoodReadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public FoodLabelReading Reading { get; set; } = new FoodLabelReading();
    }
    public class FoodAnalyzeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ScanId { get; set; }
        public FoodLabelReading Reading { get; set; } = new FoodLabelReading();
        public FoodLabelAnalysis Analysis { get; set; } = new FoodLabelAnalysis();
    }
}
namespace CarCareTracker.Models
{
    public class FoodProfileInput
    {
        public List<string> Allergens { get; set; } = new List<string>();
    }
    public class FoodPageViewModel
    {
        public bool ModelConfigured { get; set; }
        public List<FoodScan> History { get; set; } = new List<FoodScan>();
        public FoodProfile Profile { get; set; } = new FoodProfile();
    }
    public class FoodResultViewModel
    {
        public int ScanId { get; set; }
        public string ScannedBy { get; set; } = string.Empty;
        public DateTime? ScannedAtUtc { get; set; }
        public string TransactionText { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public FoodLabelReading Reading { get; set; } = new FoodLabelReading();
        public FoodLabelAnalysis Analysis { get; set; } = new FoodLabelAnalysis();
    }
}
