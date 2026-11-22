using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using System.Text;
using System.Text.Json.Nodes;

namespace CarCareTracker.Logic
{
    public interface IFoodLogic
    {
        bool ModelConfigured { get; }
        /// <summary>Reads 1 to 3 photos of one product's label with the home AI. The photos are not stored.</summary>
        Task<FoodReadResult> ReadLabel(List<byte[]> images);
        /// <summary>Analyses a checked reading for this user (their allergy profile included), optionally saving it to the household history.</summary>
        FoodAnalyzeResult Analyze(int userId, string userName, FoodLabelReading reading, bool save);
        /// <summary>A saved scan, with the allergy marks of the user who is looking at it.</summary>
        FoodScan? GetScan(int userId, int scanId);
        List<FoodScan> GetHistory(int userId, int limit = 30);
        bool DeleteScan(int userId, int scanId);
        /// <summary>Recent money out of the joint account that a scan can be linked to, newest first.</summary>
        List<BankTransaction> GetLinkableTransactions(int userId);
        OperationResponse LinkTransaction(int userId, int scanId, int transactionId);
        FoodProfile GetProfile(int userId);
        bool SaveProfile(int userId, IEnumerable<string>? allergens);
        bool DeleteProfile(int userId);
    }
    public class FoodLogic : IFoodLogic
    {
        public const int MaxImages = 3;
        public const int MaxImageBytes = 8 * 1024 * 1024;
        private readonly IAiLogic _aiLogic;
        private readonly IFoodDataAccess _foodData;
        private readonly IBudgetLogic _budgetLogic;
        private readonly IBankTransactionDataAccess _transactionData;
        private readonly ILogger<FoodLogic> _logger;
        public FoodLogic(IAiLogic aiLogic, IFoodDataAccess foodData, IBudgetLogic budgetLogic, IBankTransactionDataAccess transactionData, ILogger<FoodLogic> logger)
        {
            _aiLogic = aiLogic;
            _foodData = foodData;
            _budgetLogic = budgetLogic;
            _transactionData = transactionData;
            _logger = logger;
        }
        public bool ModelConfigured => _aiLogic.ModelConfigured;

        /// <summary>What the vision model returns. -1 means "not on the label" (strict JSON schemas cannot say null on every server).</summary>
        public class ModelLabel
        {
            public string kind { get; set; } = "not_food_label";
            public string product_name { get; set; } = string.Empty;
            public string brand { get; set; } = string.Empty;
            public string barcode { get; set; } = string.Empty;
            public string basis { get; set; } = "per_100";
            public decimal serving_size { get; set; } = -1;
            public decimal net_quantity { get; set; } = -1;
            public decimal energy_kcal { get; set; } = -1;
            public decimal energy_kj { get; set; } = -1;
            public decimal fat { get; set; } = -1;
            public decimal saturated_fat { get; set; } = -1;
            public decimal trans_fat { get; set; } = -1;
            public decimal carbohydrate { get; set; } = -1;
            public decimal sugars { get; set; } = -1;
            public decimal fibre { get; set; } = -1;
            public decimal protein { get; set; } = -1;
            public decimal salt { get; set; } = -1;
            public decimal sodium_mg { get; set; } = -1;
            public string ingredients { get; set; } = string.Empty;
            public List<string> uncertain { get; set; } = new List<string>();
        }
        public static string BuildPrompt()
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Bạn đọc ảnh nhãn thực phẩm đóng gói (tiếng Việt hoặc tiếng Anh) và trả về JSON đúng schema. Không thêm chữ nào ngoài JSON. Bạn chỉ chép lại những gì in trên nhãn, không đánh giá, không đoán.");
            prompt.AppendLine("kind: food (đồ ăn), drink (đồ uống, nhãn tính theo ml), not_food_label (ảnh không phải nhãn thực phẩm hoặc không đọc được).");
            prompt.AppendLine("Mọi trường số: số thập phân dùng dấu chấm (1,5 g = 1.5). Trường nào không có trên ảnh hoặc không đọc được thì để -1. Không được đoán.");
            prompt.AppendLine("Bảng thành phần dinh dưỡng (Nutrition Facts / Giá trị dinh dưỡng):");
            prompt.AppendLine("- Nếu bảng có cột \"100 g\" hoặc \"100 ml\" thì lấy cột đó và basis = per_100. Chỉ khi bảng chỉ có cột theo khẩu phần (per serving, 1 gói, 1 hộp...) thì basis = per_serving.");
            prompt.AppendLine("- serving_size = khối lượng một khẩu phần bằng g (đồ uống: ml); net_quantity = khối lượng tịnh cả gói bằng g hoặc ml.");
            prompt.AppendLine("- energy_kcal = năng lượng bằng kcal; nếu nhãn chỉ ghi kJ thì để energy_kcal = -1 và ghi energy_kj.");
            prompt.AppendLine("- fat = chất béo tổng (lipid); saturated_fat = chất béo bão hòa; trans_fat = chất béo chuyển hóa (trans); carbohydrate = carbohydrat/glucid/chất bột đường; sugars = đường (đường tổng, total sugars); fibre = chất xơ; protein = chất đạm.");
            prompt.AppendLine("- Các chất trên tính bằng gam. Nhãn ghi mg thì đổi ra gam (500 mg = 0.5).");
            prompt.AppendLine("- salt = muối bằng gam, chỉ khi nhãn ghi \"muối\"/\"salt\". sodium_mg = natri (sodium) bằng mg; nhãn ghi natri bằng g thì nhân 1000.");
            prompt.AppendLine("ingredients = chép nguyên văn danh sách thành phần, kể cả mã phụ gia (INS, E), phần trăm, câu \"có thể chứa\"/\"may contain\" và thông tin cảnh báo dị ứng. Chuỗi rỗng nếu ảnh không có.");
            prompt.AppendLine("product_name = tên sản phẩm; brand = nhãn hiệu; barcode = dãy số dưới mã vạch nếu đọc rõ, chuỗi rỗng nếu không.");
            prompt.AppendLine("uncertain = danh sách tên trường (vd. \"sugars\", \"sodium_mg\") mà chữ trên ảnh mờ, bị che hoặc bạn không chắc.");
            return prompt.ToString();
        }
        public static JsonObject BuildSchema()
        {
            JsonObject Text() => new JsonObject { ["type"] = "string" };
            JsonObject Num() => new JsonObject { ["type"] = "number" };
            var numbers = new[] { "serving_size", "net_quantity", "energy_kcal", "energy_kj", "fat", "saturated_fat", "trans_fat", "carbohydrate", "sugars", "fibre", "protein", "salt", "sodium_mg" };
            var properties = new JsonObject
            {
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("food", "drink", "not_food_label") },
                ["product_name"] = Text(),
                ["brand"] = Text(),
                ["barcode"] = Text(),
                ["basis"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("per_100", "per_serving") }
            };
            foreach (var name in numbers)
            {
                properties[name] = Num();
            }
            properties["ingredients"] = Text();
            properties["uncertain"] = new JsonObject { ["type"] = "array", ["items"] = Text() };
            var required = new JsonArray("kind", "product_name", "brand", "barcode", "basis");
            foreach (var name in numbers)
            {
                required.Add(name);
            }
            required.Add("ingredients");
            required.Add("uncertain");
            return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
        }
        private static readonly IReadOnlyDictionary<string, string> UncertainNames = new Dictionary<string, string>
        {
            ["product_name"] = nameof(FoodLabelReading.ProductName), ["brand"] = nameof(FoodLabelReading.Brand), ["barcode"] = nameof(FoodLabelReading.Barcode),
            ["serving_size"] = nameof(FoodLabelReading.ServingSize), ["net_quantity"] = nameof(FoodLabelReading.NetQuantity),
            ["energy_kcal"] = nameof(FoodLabelReading.EnergyKcal), ["energy_kj"] = nameof(FoodLabelReading.EnergyKcal),
            ["fat"] = nameof(FoodLabelReading.Fat), ["saturated_fat"] = nameof(FoodLabelReading.SaturatedFat), ["trans_fat"] = nameof(FoodLabelReading.TransFat),
            ["carbohydrate"] = nameof(FoodLabelReading.Carbohydrate), ["sugars"] = nameof(FoodLabelReading.Sugars), ["fibre"] = nameof(FoodLabelReading.Fibre),
            ["protein"] = nameof(FoodLabelReading.Protein), ["salt"] = nameof(FoodLabelReading.Salt), ["sodium_mg"] = nameof(FoodLabelReading.SodiumMg),
            ["ingredients"] = nameof(FoodLabelReading.Ingredients)
        };
        private static decimal? Known(decimal value) => value >= 0 ? value : null;
        private static int NutrientCount(ModelLabel label) => new[] { label.energy_kcal, label.energy_kj, label.fat, label.saturated_fat, label.carbohydrate, label.sugars, label.protein, label.salt, label.sodium_mg }.Count(x => x >= 0);

        /// <summary>
        /// Combines the readings of several photos of one product: the nutrition block comes from the photo that shows
        /// the most of the table, names from the first photo that has them, ingredient texts are joined.
        /// </summary>
        public static FoodLabelReading Merge(List<ModelLabel> labels)
        {
            var usable = labels.Where(x => x.kind == "food" || x.kind == "drink").ToList();
            var reading = new FoodLabelReading();
            if (!usable.Any())
            {
                return reading;
            }
            var table = usable.OrderByDescending(NutrientCount).First();
            reading.ProductName = usable.Select(x => x.product_name).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            reading.Brand = usable.Select(x => x.brand).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            reading.Barcode = usable.Select(x => x.barcode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            reading.Kind = usable.Any(x => x.kind == "drink") ? FoodKind.Drink : FoodKind.Food;
            reading.Basis = table.basis == "per_serving" ? NutrientBasis.PerServing : NutrientBasis.Per100;
            reading.ServingSize = Known(table.serving_size) ?? usable.Select(x => Known(x.serving_size)).FirstOrDefault(x => x.HasValue);
            reading.NetQuantity = usable.Select(x => Known(x.net_quantity)).FirstOrDefault(x => x.HasValue);
            reading.EnergyKcal = Known(table.energy_kcal) ?? (table.energy_kj >= 0 ? Math.Round(table.energy_kj / 4.184m, 0) : null);
            reading.Fat = Known(table.fat);
            reading.SaturatedFat = Known(table.saturated_fat);
            reading.TransFat = Known(table.trans_fat);
            reading.Carbohydrate = Known(table.carbohydrate);
            reading.Sugars = Known(table.sugars);
            reading.Fibre = Known(table.fibre);
            reading.Protein = Known(table.protein);
            reading.Salt = Known(table.salt);
            reading.SodiumMg = Known(table.sodium_mg);
            reading.Ingredients = string.Join("\n", usable.Select(x => (x.ingredients ?? string.Empty).Trim()).Where(x => x.Length > 0).Distinct());
            reading.Uncertain = usable.SelectMany(x => x.uncertain ?? new List<string>())
                .Select(x => UncertainNames.TryGetValue(x.Trim().ToLowerInvariant(), out var name) ? name : string.Empty)
                .Where(x => x.Length > 0).Distinct().ToList();
            return FoodLabelRules.Clean(reading);
        }

        public async Task<FoodReadResult> ReadLabel(List<byte[]> images)
        {
            if (!ModelConfigured)
            {
                return new FoodReadResult { Message = "Reading images needs the home AI, which is not turned on" };
            }
            if (images == null || images.Count == 0)
            {
                return new FoodReadResult { Message = "Take a photo of the label first" };
            }
            var labels = new List<ModelLabel>();
            var prompt = BuildPrompt();
            var schema = BuildSchema();
            foreach (var image in images.Take(MaxImages))
            {
                var label = await _aiLogic.AskVision<ModelLabel>(prompt, "Đọc nhãn thực phẩm trong ảnh này.", schema, Convert.ToBase64String(image));
                if (label != null)
                {
                    labels.Add(label);
                }
            }
            if (!labels.Any())
            {
                return new FoodReadResult { Message = "The home AI did not answer. Please try again later" };
            }
            var reading = Merge(labels);
            bool hasNumbers = new[] { reading.EnergyKcal, reading.Fat, reading.SaturatedFat, reading.Carbohydrate, reading.Sugars, reading.Protein, reading.Salt, reading.SodiumMg }.Any(x => x.HasValue);
            if (!hasNumbers && string.IsNullOrWhiteSpace(reading.Ingredients))
            {
                return new FoodReadResult { Message = "This does not look like a food label. Try a closer, sharper photo of the nutrition table or the ingredient list" };
            }
            return new FoodReadResult { Success = true, Reading = reading };
        }

        private List<string> ProfileAllergens(int userId) => _foodData.GetProfile(userId)?.Allergens ?? new List<string>();
        /// <summary>The allergy marks belong to whoever is looking, so they are set at read time and never stored.</summary>
        private FoodScan WithProfile(FoodScan scan, List<string> allergens)
        {
            foreach (var allergen in scan.Analysis.Allergens)
            {
                allergen.InProfile = allergens.Contains(allergen.Group);
            }
            return scan;
        }
        public FoodAnalyzeResult Analyze(int userId, string userName, FoodLabelReading reading, bool save)
        {
            var cleaned = FoodLabelRules.Clean(reading ?? new FoodLabelReading());
            var result = new FoodAnalyzeResult { Success = true, Reading = cleaned };
            //stored without profile marks: the history is shared with the household.
            var shared = FoodLabelRules.Analyze(cleaned);
            if (save)
            {
                var scan = new FoodScan
                {
                    HouseholdId = _budgetLogic.GetHouseholdId(userId),
                    UserId = userId,
                    UserName = userName ?? string.Empty,
                    CreatedAtUtc = DateTime.UtcNow,
                    Reading = cleaned,
                    Analysis = shared
                };
                result.ScanId = _foodData.AddScan(scan);
                if (result.ScanId == 0)
                {
                    result.Message = "The scan could not be saved";
                }
            }
            result.Analysis = FoodLabelRules.Analyze(cleaned, ProfileAllergens(userId));
            return result;
        }
        public FoodScan? GetScan(int userId, int scanId)
        {
            var scan = _foodData.GetScan(_budgetLogic.GetHouseholdId(userId), scanId);
            return scan == null ? null : WithProfile(scan, ProfileAllergens(userId));
        }
        public List<FoodScan> GetHistory(int userId, int limit = 30)
        {
            var allergens = ProfileAllergens(userId);
            return _foodData.GetScans(_budgetLogic.GetHouseholdId(userId), Math.Clamp(limit, 1, 200)).Select(x => WithProfile(x, allergens)).ToList();
        }
        public bool DeleteScan(int userId, int scanId)
        {
            return _foodData.DeleteScan(_budgetLogic.GetHouseholdId(userId), scanId);
        }
        public List<BankTransaction> GetLinkableTransactions(int userId)
        {
            return _transactionData.GetSince(_budgetLogic.GetHouseholdId(userId), DateTime.UtcNow.AddDays(-30))
                .Where(x => x.Direction == TransferDirection.Out)
                .OrderByDescending(x => x.OccurredAtUtc)
                .Take(40)
                .ToList();
        }
        public static string TransactionText(BankTransaction transaction)
        {
            var date = (transaction.OccurredAtUtc + SepayRules.VietnamOffset).ToString("dd/MM/yyyy HH:mm");
            var content = transaction.Content ?? string.Empty;
            if (content.Length > 60)
            {
                content = content.Substring(0, 60) + "…";
            }
            return $"{date} · {transaction.Amount:N0} đ · {content}".TrimEnd(' ', '·');
        }
        public OperationResponse LinkTransaction(int userId, int scanId, int transactionId)
        {
            var householdId = _budgetLogic.GetHouseholdId(userId);
            var scan = _foodData.GetScan(householdId, scanId);
            if (scan == null)
            {
                return OperationResponse.Failed("Scan not found");
            }
            if (transactionId == 0)
            {
                scan.TransactionId = 0;
                scan.TransactionText = string.Empty;
            }
            else
            {
                var transaction = _transactionData.GetSince(householdId, DateTime.UtcNow.AddDays(-400)).FirstOrDefault(x => x.Id == transactionId);
                if (transaction == null)
                {
                    return OperationResponse.Failed("Transaction not found");
                }
                scan.TransactionId = transaction.Id;
                scan.TransactionText = TransactionText(transaction);
            }
            return _foodData.UpdateScan(scan) ? OperationResponse.Succeed(transactionId == 0 ? "Link removed" : "Linked to the transaction") : OperationResponse.Failed();
        }
        public FoodProfile GetProfile(int userId)
        {
            return _foodData.GetProfile(userId) ?? new FoodProfile { Id = userId };
        }
        public bool SaveProfile(int userId, IEnumerable<string>? allergens)
        {
            var keys = FoodLabelRules.CleanAllergenKeys(allergens);
            if (!keys.Any())
            {
                //an empty profile is no profile: nothing about the user's health is kept.
                _foodData.DeleteProfile(userId);
                return true;
            }
            return _foodData.SaveProfile(new FoodProfile { Id = userId, Allergens = keys, UpdatedAtUtc = DateTime.UtcNow });
        }
        public bool DeleteProfile(int userId)
        {
            _foodData.DeleteProfile(userId);
            return true;
        }
    }
}
