using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CarCareTracker.Logic
{
    public interface IAiLogic
    {
        bool ModelConfigured { get; }
        Task<AiChatResponse> ProcessMessage(string message, List<Vehicle> vehicles, UserConfig userConfig);
        Task<AiChatResponse> ProcessImage(string tempLocation, string fileName, List<Vehicle> vehicles, UserConfig userConfig);
        int GetRecordVehicleId(ImportMode recordType, int recordId);
        OperationResponse AttachPayment(AiAttachPaymentRequest request, UserConfig userConfig);
        OperationResponse SaveDraft(AiDraft draft, Vehicle vehicle, UserConfig userConfig, int userId, string userName);
    }
    /// <summary>
    /// Turns a chat message into a draft record or an answer.
    /// A locally hosted model (Ollama) decides what the message means; numbers, dates and vehicle names are also
    /// extracted by deterministic rules, which take precedence and act as a fallback when no model is available.
    /// </summary>
    public class AiLogic : IAiLogic
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ITranslationHelper _translator;
        private readonly IVehicleLogic _vehicleLogic;
        private readonly IOdometerLogic _odometerLogic;
        private readonly IMaintenanceLogic _maintenanceLogic;
        private readonly IReminderHelper _reminderHelper;
        private readonly IEventLogic _eventLogic;
        private readonly IServiceRecordDataAccess _serviceRecordDataAccess;
        private readonly IGasRecordDataAccess _gasRecordDataAccess;
        private readonly ITaxRecordDataAccess _taxRecordDataAccess;
        private readonly IReminderRecordDataAccess _reminderRecordDataAccess;
        private readonly ICollisionRecordDataAccess _collisionRecordDataAccess;
        private readonly IUpgradeRecordDataAccess _upgradeRecordDataAccess;
        private readonly IFileHelper _fileHelper;
        private readonly ILogger<AiLogic> _logger;
        public const string PaidTag = "da-thanh-toan";
        private const string ReferenceLabel = "Mã GD";
        //a home server has one GPU at best: run one model request at a time.
        private static readonly SemaphoreSlim _modelLock = new SemaphoreSlim(1, 1);
        public AiLogic(IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ITranslationHelper translator,
            IVehicleLogic vehicleLogic,
            IOdometerLogic odometerLogic,
            IMaintenanceLogic maintenanceLogic,
            IReminderHelper reminderHelper,
            IEventLogic eventLogic,
            IServiceRecordDataAccess serviceRecordDataAccess,
            IGasRecordDataAccess gasRecordDataAccess,
            ITaxRecordDataAccess taxRecordDataAccess,
            IReminderRecordDataAccess reminderRecordDataAccess,
            ICollisionRecordDataAccess collisionRecordDataAccess,
            IUpgradeRecordDataAccess upgradeRecordDataAccess,
            IFileHelper fileHelper,
            ILogger<AiLogic> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _translator = translator;
            _vehicleLogic = vehicleLogic;
            _odometerLogic = odometerLogic;
            _maintenanceLogic = maintenanceLogic;
            _reminderHelper = reminderHelper;
            _eventLogic = eventLogic;
            _serviceRecordDataAccess = serviceRecordDataAccess;
            _gasRecordDataAccess = gasRecordDataAccess;
            _taxRecordDataAccess = taxRecordDataAccess;
            _reminderRecordDataAccess = reminderRecordDataAccess;
            _collisionRecordDataAccess = collisionRecordDataAccess;
            _upgradeRecordDataAccess = upgradeRecordDataAccess;
            _fileHelper = fileHelper;
            _logger = logger;
        }
        private string ModelUrl => (_configuration["LUBELOGGER_AI_URL"] ?? string.Empty).TrimEnd('/');
        private string ModelName => _configuration["LUBELOGGER_AI_MODEL"] ?? "qwen2.5:7b";
        private string VisionModelName => _configuration["LUBELOGGER_AI_VISION_MODEL"] ?? "qwen2.5vl:7b";
        public bool ModelConfigured => !string.IsNullOrWhiteSpace(ModelUrl);

        private class ModelResult
        {
            public string intent { get; set; } = "unknown";
            public int vehicle_id { get; set; }
            public string date { get; set; } = string.Empty;
            public decimal odometer { get; set; }
            public decimal cost { get; set; }
            public decimal liters { get; set; }
            public List<string> items { get; set; } = new List<string>();
            public string description { get; set; } = string.Empty;
        }
        private static string VehicleName(Vehicle vehicle)
        {
            return $"{vehicle.Make} {vehicle.Model}".Trim();
        }
        private string BuildSystemPrompt(List<Vehicle> vehicles, DateTime today)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Bạn là trợ lý ghi chép chi phí và bảo dưỡng xe của một gia đình Việt Nam.");
            prompt.AppendLine("Nhiệm vụ: đọc MỘT tin nhắn và trả về JSON đúng theo schema. Không giải thích, không thêm chữ nào ngoài JSON.");
            prompt.AppendLine($"Hôm nay là {today:yyyy-MM-dd}.");
            prompt.AppendLine();
            prompt.AppendLine("Danh sách xe (vehicle_id | tên | biển số | loại | tên gọi khác):");
            foreach (var vehicle in vehicles)
            {
                prompt.AppendLine($"{vehicle.Id} | {VehicleName(vehicle)} | {vehicle.LicensePlate} | {vehicle.VehicleCategory} | {string.Join(", ", vehicle.Tags)}");
            }
            prompt.AppendLine();
            prompt.AppendLine("intent:");
            prompt.AppendLine("- service: bảo dưỡng, thay thế phụ tùng (thay dầu, lốp, ắc quy...)");
            prompt.AppendLine("- fuel: đổ xăng, đổ dầu diesel, sạc điện");
            prompt.AppendLine("- expense: chi phí khác (gửi xe, phí cầu đường, rửa xe, đăng kiểm, bảo hiểm, phạt...)");
            prompt.AppendLine("- odometer: chỉ báo số km hiện tại");
            prompt.AppendLine("- query_reminders: hỏi khi nào đến hạn bảo dưỡng, việc gì sắp phải làm");
            prompt.AppendLine("- query_spending: hỏi đã tiêu bao nhiêu tiền");
            prompt.AppendLine("- unknown: không thuộc các loại trên");
            prompt.AppendLine();
            prompt.AppendLine("items: chỉ dùng các mã sau, chọn mọi mã được nhắc tới.");
            prompt.AppendLine("Bảo dưỡng: " + string.Join("; ", MaintenanceCatalog.Items.Select(x => $"{x.Tag} = {_translator.Translate("vi_VN", x.Name)}")));
            prompt.AppendLine("Chi phí khác: " + string.Join("; ", ExpenseCatalog.Categories.Select(x => $"{x.Tag} = {_translator.Translate("vi_VN", x.Name)}")));
            prompt.AppendLine();
            prompt.AppendLine("Quy tắc:");
            prompt.AppendLine("- vehicle_id: mã xe khớp với tin nhắn; 0 nếu không rõ xe nào.");
            prompt.AppendLine("- date: yyyy-MM-dd; chuỗi rỗng nếu tin nhắn không nói ngày. \"hôm qua\" là ngày trước hôm nay.");
            prompt.AppendLine("- cost: số tiền bằng đồng. 120k = 120000; 1tr2 = 1200000; 1,5 triệu = 1500000; 800 nghìn = 800000. 0 nếu không có.");
            prompt.AppendLine("- odometer: số km trên đồng hồ; 12.350km = 12350; 0 nếu không có.");
            prompt.AppendLine("- liters: số lít nhiên liệu; 0 nếu không có.");
            prompt.AppendLine("- description: mô tả ngắn gọn bằng tiếng Việt có dấu, không chứa số tiền hay số km.");
            return prompt.ToString();
        }
        private static JsonObject BuildSchema()
        {
            var tags = MaintenanceCatalog.Items.Select(x => x.Tag).Union(ExpenseCatalog.Categories.Select(x => x.Tag)).Distinct();
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["intent"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("service", "fuel", "expense", "odometer", "query_reminders", "query_spending", "unknown") },
                    ["vehicle_id"] = new JsonObject { ["type"] = "integer" },
                    ["date"] = new JsonObject { ["type"] = "string" },
                    ["odometer"] = new JsonObject { ["type"] = "number" },
                    ["cost"] = new JsonObject { ["type"] = "number" },
                    ["liters"] = new JsonObject { ["type"] = "number" },
                    ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(tags.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) } },
                    ["description"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray("intent", "vehicle_id", "date", "odometer", "cost", "liters", "items", "description")
            };
        }
        //"ollama" talks to Ollama's /api/chat; "openai" talks to any OpenAI-compatible server
        //(llama.cpp llama-server, LM Studio, vLLM...) at /v1/chat/completions.
        private bool UseOpenAiApi
        {
            get
            {
                var api = (_configuration["LUBELOGGER_AI_API"] ?? string.Empty).Trim().ToLowerInvariant();
                if (api == "openai" || api == "llamacpp" || api == "llama.cpp")
                {
                    return true;
                }
                if (api == "ollama")
                {
                    return false;
                }
                return ModelUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
            }
        }
        private string ChatEndpoint
        {
            get
            {
                if (!UseOpenAiApi)
                {
                    return $"{ModelUrl}/api/chat";
                }
                return ModelUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{ModelUrl}/chat/completions" : $"{ModelUrl}/v1/chat/completions";
            }
        }
        private static string MimeFromBase64(string imageBase64)
        {
            //PNG files start with 0x89 'P' 'N' 'G', which is "iVBOR" in base64.
            return imageBase64.StartsWith("iVBOR") ? "image/png" : "image/jpeg";
        }
        private JsonObject BuildOpenAiRequest(string modelName, string systemPrompt, string userMessage, JsonObject schema, string? imageBase64)
        {
            JsonNode userContent;
            if (!string.IsNullOrWhiteSpace(imageBase64))
            {
                userContent = new JsonArray(
                    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{MimeFromBase64(imageBase64)};base64,{imageBase64}" } },
                    new JsonObject { ["type"] = "text", ["text"] = userMessage });
            }
            else
            {
                userContent = JsonValue.Create(userMessage)!;
            }
            return new JsonObject
            {
                ["model"] = modelName,
                ["stream"] = false,
                ["temperature"] = 0,
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = "result", ["strict"] = true, ["schema"] = schema.DeepClone() }
                },
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = systemPrompt }, new JsonObject { ["role"] = "user", ["content"] = userContent })
            };
        }
        private JsonObject BuildOllamaRequest(string modelName, string systemPrompt, string userMessage, JsonObject schema, string? imageBase64)
        {
            var userEntry = new JsonObject { ["role"] = "user", ["content"] = userMessage };
            if (!string.IsNullOrWhiteSpace(imageBase64))
            {
                userEntry["images"] = new JsonArray(imageBase64);
            }
            return new JsonObject
            {
                ["model"] = modelName,
                ["stream"] = false,
                ["format"] = schema.DeepClone(),
                ["options"] = new JsonObject { ["temperature"] = 0 },
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = systemPrompt }, userEntry)
            };
        }
        //models sometimes wrap the JSON in a markdown fence or add a sentence around it.
        public static string ExtractJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            return start >= 0 && end > start ? content.Substring(start, end - start + 1) : content.Trim();
        }
        private async Task<T?> AskModel<T>(string modelName, string systemPrompt, string userMessage, JsonObject schema, string? imageBase64 = null) where T : class
        {
            bool openAi = UseOpenAiApi;
            var request = openAi ? BuildOpenAiRequest(modelName, systemPrompt, userMessage, schema, imageBase64) : BuildOllamaRequest(modelName, systemPrompt, userMessage, schema, imageBase64);
            await _modelLock.WaitAsync();
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(int.TryParse(_configuration["LUBELOGGER_AI_TIMEOUT"], out int timeout) ? timeout : 180);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint)
                {
                    Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json")
                };
                var apiKey = _configuration["LUBELOGGER_AI_KEY"];
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }
                var response = await client.SendAsync(httpRequest);
                var responseText = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"AI model request failed: {(int)response.StatusCode} {responseText}");
                    return null;
                }
                var body = JsonNode.Parse(responseText);
                var content = (openAi ? body?["choices"]?[0]?["message"]?["content"] : body?["message"]?["content"])?.GetValue<string>() ?? string.Empty;
                return JsonSerializer.Deserialize<T>(ExtractJson(content), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString });
            }
            catch (Exception ex)
            {
                _logger.LogError($"AI model request failed: {ex.Message}");
                return null;
            }
            finally
            {
                _modelLock.Release();
            }
        }
        private Task<ModelResult?> AskModel(string message, List<Vehicle> vehicles, DateTime today)
        {
            return AskModel<ModelResult>(ModelName, BuildSystemPrompt(vehicles, today), message, BuildSchema());
        }
        private static AiIntent ToIntent(string intent)
        {
            return intent switch
            {
                "service" => AiIntent.Service,
                "fuel" => AiIntent.Fuel,
                "expense" => AiIntent.Expense,
                "odometer" => AiIntent.Odometer,
                "query_reminders" => AiIntent.QueryReminders,
                "query_spending" => AiIntent.QuerySpending,
                _ => AiIntent.Unknown
            };
        }
        public async Task<AiChatResponse> ProcessMessage(string message, List<Vehicle> vehicles, UserConfig userConfig)
        {
            var language = userConfig.UserLanguage;
            var response = new AiChatResponse();
            if (string.IsNullOrWhiteSpace(message))
            {
                response.Reply = _translator.Translate(language, "Please type a message");
                return response;
            }
            if (!vehicles.Any())
            {
                response.Reply = _translator.Translate(language, "Add a vehicle first");
                return response;
            }
            var today = DateTime.Now.Date;
            var parsed = VietnameseTextParser.Parse(message, vehicles, today);
            var intent = parsed.Intent;
            int vehicleId = parsed.VehicleId;
            DateTime date = parsed.Date ?? today;
            int mileage = parsed.Mileage ?? 0;
            decimal cost = parsed.Cost ?? 0;
            decimal fuelAmount = parsed.FuelAmount ?? 0;
            var maintenanceTags = parsed.MaintenanceTags.ToList();
            var expenseTags = parsed.ExpenseTags.ToList();
            string modelDescription = string.Empty;
            if (ModelConfigured)
            {
                var model = await AskModel(message, vehicles, today);
                if (model != null)
                {
                    response.Source = "ai";
                    var modelIntent = ToIntent(model.intent);
                    if (modelIntent != AiIntent.Unknown)
                    {
                        intent = modelIntent;
                    }
                    if (vehicleId == 0 && vehicles.Any(x => x.Id == model.vehicle_id))
                    {
                        vehicleId = model.vehicle_id;
                    }
                    if (!parsed.Date.HasValue && DateTime.TryParseExact(model.date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime modelDate) && modelDate <= today.AddDays(1) && modelDate > today.AddYears(-5))
                    {
                        date = modelDate;
                    }
                    //numbers found by the rules are trusted over the model's; disagreement is surfaced to the user.
                    if (!parsed.Cost.HasValue && model.cost > 0) { cost = model.cost; }
                    else if (parsed.Cost.HasValue && model.cost > 0 && model.cost != parsed.Cost.Value) { response.Warnings.Add(_translator.Translate(language, "Please double check the cost")); }
                    if (!parsed.Mileage.HasValue && model.odometer > 0) { mileage = (int)model.odometer; }
                    else if (parsed.Mileage.HasValue && model.odometer > 0 && (int)model.odometer != parsed.Mileage.Value) { response.Warnings.Add(_translator.Translate(language, "Please double check the odometer")); }
                    if (!parsed.FuelAmount.HasValue && model.liters > 0) { fuelAmount = model.liters; }
                    foreach (var item in model.items ?? new List<string>())
                    {
                        if (MaintenanceCatalog.IsMaintenanceTag(item) && intent == AiIntent.Service && !maintenanceTags.Contains(item)) { maintenanceTags.Add(item); }
                        else if (ExpenseCatalog.Categories.Any(x => x.Tag == item) && intent == AiIntent.Expense && !expenseTags.Contains(item)) { expenseTags.Add(item); }
                    }
                    modelDescription = (model.description ?? string.Empty).Trim();
                }
            }
            var vehicle = vehicles.FirstOrDefault(x => x.Id == vehicleId);
            switch (intent)
            {
                case AiIntent.QueryReminders:
                    response.Reply = AnswerReminders(vehicle == null ? vehicles : new List<Vehicle> { vehicle }, maintenanceTags.Union(expenseTags).ToList(), language);
                    return response;
                case AiIntent.QuerySpending:
                    response.Reply = AnswerSpending(vehicle == null ? vehicles : new List<Vehicle> { vehicle }, parsed, today, language);
                    return response;
                case AiIntent.Unknown:
                    response.Reply = _translator.Translate(language, "Sorry, I could not understand. Try something like: oil change Vision at 12,350 km for 120k");
                    return response;
            }
            var draft = new AiDraft
            {
                Intent = intent,
                VehicleId = vehicleId,
                Date = date.ToShortDateString(),
                Mileage = mileage,
                Cost = cost,
                FuelAmount = fuelAmount,
                Notes = $"{_translator.Translate(language, "Entered via chat")}: {message.Trim()}"
            };
            if (intent == AiIntent.Service)
            {
                draft.Tags = maintenanceTags;
                draft.Description = maintenanceTags.Any()
                    ? string.Join(", ", maintenanceTags.Select(x => _translator.Translate(language, MaintenanceCatalog.Items.First(y => y.Tag == x).Name)))
                    : modelDescription;
            }
            else if (intent == AiIntent.Expense)
            {
                if (!expenseTags.Any()) { expenseTags.Add(ExpenseCatalog.OtherTag); }
                draft.Tags = expenseTags;
                draft.Description = !string.IsNullOrWhiteSpace(modelDescription) ? modelDescription : string.Join(", ", expenseTags.Select(x => _translator.Translate(language, ExpenseCatalog.Categories.First(y => y.Tag == x).Name)));
            }
            if (string.IsNullOrWhiteSpace(draft.Description) && (intent == AiIntent.Service || intent == AiIntent.Expense))
            {
                draft.Description = message.Trim();
            }
            if (vehicle == null)
            {
                response.Warnings.Add(_translator.Translate(language, "Which vehicle is this for?"));
            }
            if (intent != AiIntent.Odometer && cost == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "No cost found"));
            }
            if (intent == AiIntent.Fuel && fuelAmount == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "No fuel amount found"));
            }
            if ((intent == AiIntent.Service || intent == AiIntent.Fuel || intent == AiIntent.Odometer) && mileage == 0 && !(vehicle?.OdometerOptional ?? false))
            {
                response.Warnings.Add(_translator.Translate(language, "No odometer reading found"));
            }
            else if (vehicle != null && mileage > 0 && intent != AiIntent.Expense)
            {
                var lastMileage = _vehicleLogic.GetMaxMileage(vehicle.Id);
                if (mileage < lastMileage && date >= today)
                {
                    response.Warnings.Add($"{_translator.Translate(language, "Odometer is lower than the last reading")}: {lastMileage}");
                }
            }
            response.IsDraft = true;
            response.Draft = draft;
            return response;
        }
        private string AnswerReminders(List<Vehicle> vehicles, List<string> tags, string language)
        {
            var lines = new List<(ReminderUrgency urgency, int dueDays, string text)>();
            foreach (var vehicle in vehicles)
            {
                var reminders = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(vehicle.Id);
                if (tags.Any())
                {
                    reminders = reminders.Where(x => x.Tags.Any(y => tags.Contains(y))).ToList();
                }
                if (!reminders.Any())
                {
                    continue;
                }
                var currentMileage = _vehicleLogic.GetMaxMileage(vehicle.Id);
                foreach (var reminder in _reminderHelper.GetReminderRecordViewModels(reminders, currentMileage, DateTime.Now))
                {
                    var due = new List<string>();
                    if (reminder.UserMetric != ReminderMetric.Odometer)
                    {
                        due.Add($"{reminder.Date.ToShortDateString()} ({reminder.DueDays} {_translator.Translate(language, "days remaining")})");
                    }
                    if (reminder.UserMetric != ReminderMetric.Date)
                    {
                        due.Add($"{reminder.Mileage} km ({reminder.DueMileage} km {_translator.Translate(language, "remaining")})");
                    }
                    var urgency = _translator.Translate(language, StaticHelper.GetTitleCaseReminderUrgency(reminder.Urgency));
                    lines.Add((reminder.Urgency, reminder.DueDays, $"{VehicleName(vehicle)} - {reminder.Description}: {string.Join(" / ", due)} [{urgency}]"));
                }
            }
            if (!lines.Any())
            {
                return _translator.Translate(language, "No Reminders");
            }
            return string.Join("\n", lines.OrderByDescending(x => x.urgency).ThenBy(x => x.dueDays).Take(tags.Any() ? 10 : 5).Select(x => x.text));
        }
        private string AnswerSpending(List<Vehicle> vehicles, ParsedMessage parsed, DateTime today, string language)
        {
            DateTime from = new DateTime(today.Year, today.Month, 1);
            DateTime to = from.AddMonths(1);
            if (parsed.MentionsThisYear)
            {
                from = new DateTime(today.Year, 1, 1);
                to = from.AddYears(1);
            }
            else if (parsed.MentionsLastMonth)
            {
                from = from.AddMonths(-1);
                to = from.AddMonths(1);
            }
            var lines = new List<string>();
            decimal total = 0;
            foreach (var vehicle in vehicles)
            {
                var records = _vehicleLogic.GetVehicleRecords(vehicle.Id);
                decimal vehicleTotal = records.ServiceRecords.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Cost)
                    + records.CollisionRecords.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Cost)
                    + records.UpgradeRecords.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Cost)
                    + records.GasRecords.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Cost)
                    + records.TaxRecords.Where(x => x.Date >= from && x.Date < to).Sum(x => x.Cost);
                total += vehicleTotal;
                if (vehicleTotal > 0)
                {
                    lines.Add($"{VehicleName(vehicle)}: {vehicleTotal.ToString("C")}");
                }
            }
            var header = $"{from.ToShortDateString()} - {to.AddDays(-1).ToShortDateString()}: {_translator.Translate(language, "Total")} {total.ToString("C")}";
            return lines.Count > 1 ? $"{header}\n{string.Join("\n", lines)}" : header;
        }
        private class ImageLine
        {
            public string name { get; set; } = string.Empty;
            public decimal amount { get; set; }
        }
        private class ImageResult
        {
            public string kind { get; set; } = "other";
            public string vendor { get; set; } = string.Empty;
            public string date { get; set; } = string.Empty;
            public decimal total { get; set; }
            public decimal odometer { get; set; }
            public string plate { get; set; } = string.Empty;
            public List<ImageLine> lines { get; set; } = new List<ImageLine>();
            public string bank { get; set; } = string.Empty;
            public string reference { get; set; } = string.Empty;
            public string recipient { get; set; } = string.Empty;
            public string content { get; set; } = string.Empty;
        }
        private static string BuildImagePrompt(DateTime today)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Bạn đọc ảnh chụp liên quan đến chi phí xe của một gia đình Việt Nam và trả về JSON đúng theo schema. Không thêm chữ nào ngoài JSON.");
            prompt.AppendLine($"Hôm nay là {today:yyyy-MM-dd}.");
            prompt.AppendLine("kind:");
            prompt.AppendLine("- receipt: hoá đơn, phiếu sửa chữa/bảo dưỡng, biên lai của garage, cửa hàng phụ tùng, cây xăng, bãi gửi xe...");
            prompt.AppendLine("- transfer: ảnh chụp màn hình giao dịch chuyển khoản thành công của ngân hàng hoặc ví điện tử");
            prompt.AppendLine("- other: không phải hai loại trên hoặc không đọc được");
            prompt.AppendLine("Quy tắc chung:");
            prompt.AppendLine("- Mọi số tiền là số nguyên tính bằng đồng, không có dấu phân cách: 1.200.000đ = 1200000.");
            prompt.AppendLine("- date: ngày trên chứng từ, dạng yyyy-MM-dd; chuỗi rỗng nếu không thấy.");
            prompt.AppendLine("- Trường nào không có trên ảnh thì để chuỗi rỗng hoặc 0. Không được đoán.");
            prompt.AppendLine("Với receipt: vendor = tên cửa hàng; total = tổng tiền phải trả (đã gồm VAT); odometer = số km chỉ khi phiếu ghi rõ \"Km\" hoặc \"số km\", là số nguyên; không lấy số trong biển số, số điện thoại hay số hoá đơn, không có thì 0; plate = biển số xe; lines = từng dòng hàng hoá/dịch vụ với name (giữ nguyên chữ trên phiếu) và amount (thành tiền của dòng).");
            prompt.AppendLine("Với transfer: total = số tiền chuyển; bank = tên ngân hàng/ví của người chuyển; reference = mã giao dịch; recipient = TÊN người nhận; content = nội dung chuyển khoản. KHÔNG ghi số tài khoản vào bất kỳ trường nào.");
            return prompt.ToString();
        }
        private static JsonObject BuildImageSchema()
        {
            JsonObject Text() => new JsonObject { ["type"] = "string" };
            JsonObject Num() => new JsonObject { ["type"] = "number" };
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("receipt", "transfer", "other") },
                    ["vendor"] = Text(), ["date"] = Text(), ["total"] = Num(), ["odometer"] = Num(), ["plate"] = Text(),
                    ["lines"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["name"] = Text(), ["amount"] = Num() }, ["required"] = new JsonArray("name", "amount") }
                    },
                    ["bank"] = Text(), ["reference"] = Text(), ["recipient"] = Text(), ["content"] = Text()
                },
                ["required"] = new JsonArray("kind", "vendor", "date", "total", "odometer", "plate", "lines", "bank", "reference", "recipient", "content")
            };
        }
        private static string Clean(string? value, int maxLength = 200)
        {
            var cleaned = System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }
        /// <summary>
        /// Long digit runs in free text of a transfer are most likely account or card numbers: keep the last four digits only.
        /// </summary>
        private static string MaskAccountNumbers(string value)
        {
            return System.Text.RegularExpressions.Regex.Replace(value, @"\d{8,}", m => new string('*', 4) + m.Value.Substring(m.Value.Length - 4));
        }
        private static bool HasReference(string notes, string reference)
        {
            return !string.IsNullOrWhiteSpace(reference) && (notes ?? string.Empty).Contains($"{ReferenceLabel}: {reference}", StringComparison.OrdinalIgnoreCase);
        }
        private string PaymentNote(AiPayment payment, string language)
        {
            var parts = new List<string> { $"{_translator.Translate(language, "Paid by transfer")} {payment.Amount.ToString("C")}" };
            if (!string.IsNullOrWhiteSpace(payment.Date)) { parts.Add(payment.Date); }
            if (!string.IsNullOrWhiteSpace(payment.Bank)) { parts.Add(payment.Bank); }
            if (!string.IsNullOrWhiteSpace(payment.Reference)) { parts.Add($"{ReferenceLabel}: {payment.Reference}"); }
            if (!string.IsNullOrWhiteSpace(payment.Recipient)) { parts.Add($"{_translator.Translate(language, "Recipient")}: {payment.Recipient}"); }
            if (!string.IsNullOrWhiteSpace(payment.Content)) { parts.Add($"{_translator.Translate(language, "Transfer content")}: {payment.Content}"); }
            return string.Join(" · ", parts);
        }
        //vision models sometimes copy the digits of the plate (59X2-678.90) into the odometer.
        public static bool OdometerFromPlate(decimal odometer, string? plate)
        {
            var plateDigits = new string((plate ?? string.Empty).Where(char.IsDigit).ToArray());
            var odometerDigits = ((long)Math.Floor(odometer)).ToString(CultureInfo.InvariantCulture);
            return plateDigits.Length >= 3 && odometerDigits.Length >= 3 && plateDigits.Contains(odometerDigits);
        }
        private static bool IsValidTempLocation(string location)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(location ?? string.Empty, @"^/temp/[A-Za-z0-9-]+\.(png|jpg|jpeg)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public async Task<AiChatResponse> ProcessImage(string tempLocation, string fileName, List<Vehicle> vehicles, UserConfig userConfig)
        {
            var response = await ReadImage(tempLocation, fileName, vehicles, userConfig);
            if (!response.IsDraft && IsValidTempLocation(tempLocation))
            {
                //nothing to confirm, so the image is not needed any more.
                _fileHelper.DeleteFile(tempLocation);
            }
            return response;
        }
        private async Task<AiChatResponse> ReadImage(string tempLocation, string fileName, List<Vehicle> vehicles, UserConfig userConfig)
        {
            var language = userConfig.UserLanguage;
            var response = new AiChatResponse();
            if (!ModelConfigured)
            {
                response.Reply = _translator.Translate(language, "Reading images needs the home AI, which is not turned on");
                return response;
            }
            if (!vehicles.Any())
            {
                response.Reply = _translator.Translate(language, "Add a vehicle first");
                return response;
            }
            var fullPath = IsValidTempLocation(tempLocation) ? _fileHelper.GetFullFilePath(tempLocation) : string.Empty;
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                response.Reply = _translator.Translate(language, "The image could not be read. Please use a PNG or JPG file");
                return response;
            }
            var today = DateTime.Now.Date;
            var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(fullPath));
            var image = await AskModel<ImageResult>(VisionModelName, BuildImagePrompt(today), "Đọc ảnh này.", BuildImageSchema(), imageBase64);
            if (image == null)
            {
                response.Reply = _translator.Translate(language, "The home AI did not answer. Please try again later");
                return response;
            }
            response.Source = "ai";
            if (image.kind != "receipt" && image.kind != "transfer")
            {
                response.Reply = _translator.Translate(language, "This does not look like a receipt or a bank transfer");
                return response;
            }
            DateTime date = today;
            bool hasDate = DateTime.TryParseExact(image.date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime imageDate) && imageDate <= today.AddDays(1) && imageDate > today.AddYears(-5);
            if (hasDate)
            {
                date = imageDate;
            }
            else
            {
                response.Warnings.Add(_translator.Translate(language, "No date found, using today"));
            }
            var files = new List<UploadedFiles> { new UploadedFiles { Name = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(tempLocation) : Path.GetFileName(fileName), Location = tempLocation, IsPending = true } };
            var lines = (image.lines ?? new List<ImageLine>()).Where(x => !string.IsNullOrWhiteSpace(x.name)).ToList();
            decimal total = image.total > 0 ? Math.Round(image.total) : 0;
            if (image.kind == "transfer")
            {
                var payment = new AiPayment
                {
                    Amount = total,
                    AmountText = total.ToString("C"),
                    Date = date.ToShortDateString(),
                    Bank = Clean(image.bank, 60),
                    Reference = Clean(image.reference, 60),
                    Recipient = MaskAccountNumbers(Clean(image.recipient, 80)),
                    Content = MaskAccountNumbers(Clean(image.content))
                };
                response.Payment = payment;
                if (payment.Amount <= 0)
                {
                    response.Reply = _translator.Translate(language, "Could not read the transfer amount");
                    return response;
                }
                //look for the same transaction, and for records this transfer may be paying for.
                foreach (var vehicle in vehicles)
                {
                    var records = _vehicleLogic.GetVehicleRecords(vehicle.Id);
                    var candidates = records.ServiceRecords.Select(x => (type: ImportMode.ServiceRecord, x.Id, x.Date, x.Description, x.Cost, x.Notes))
                        .Concat(records.CollisionRecords.Select(x => (type: ImportMode.RepairRecord, x.Id, x.Date, x.Description, x.Cost, x.Notes)))
                        .Concat(records.UpgradeRecords.Select(x => (type: ImportMode.UpgradeRecord, x.Id, x.Date, x.Description, x.Cost, x.Notes)))
                        .Concat(records.GasRecords.Select(x => (type: ImportMode.GasRecord, x.Id, x.Date, Description: _translator.Translate(language, "Fuel"), x.Cost, x.Notes)))
                        .Concat(records.TaxRecords.Select(x => (type: ImportMode.TaxRecord, x.Id, x.Date, x.Description, x.Cost, x.Notes)));
                    foreach (var candidate in candidates)
                    {
                        if (HasReference(candidate.Notes, payment.Reference))
                        {
                            response.Payment = null;
                            response.Reply = $"{_translator.Translate(language, "This transfer has already been recorded")}: {VehicleName(vehicle)} - {candidate.Description} ({candidate.Date.ToShortDateString()})";
                            return response;
                        }
                        bool sameAmount = candidate.Cost > 0 && Math.Abs(candidate.Cost - payment.Amount) <= payment.Amount * 0.01m;
                        bool closeDate = Math.Abs((candidate.Date.Date - date).TotalDays) <= 3;
                        bool unpaid = !(candidate.Notes ?? string.Empty).Contains($"{ReferenceLabel}:");
                        if (sameAmount && closeDate && unpaid)
                        {
                            response.Matches.Add(new AiPaymentMatch { RecordType = candidate.type, RecordId = candidate.Id, VehicleId = vehicle.Id, VehicleName = VehicleName(vehicle), Date = candidate.Date.ToShortDateString(), Description = candidate.Description, Cost = candidate.Cost, CostText = candidate.Cost.ToString("C") });
                        }
                    }
                }
                //no record yet: propose a new one from the transfer content.
                var parsedContent = VietnameseTextParser.Parse($"{payment.Content} {payment.Recipient}", vehicles, today);
                var transferIntent = parsedContent.Intent == AiIntent.Service ? AiIntent.Service : AiIntent.Expense;
                var transferTags = transferIntent == AiIntent.Service ? parsedContent.MaintenanceTags.ToList() : parsedContent.ExpenseTags.ToList();
                string transferDescription;
                if (transferIntent == AiIntent.Service)
                {
                    transferDescription = string.Join(", ", transferTags.Select(x => _translator.Translate(language, MaintenanceCatalog.Items.First(y => y.Tag == x).Name)));
                }
                else
                {
                    if (!transferTags.Any()) { transferTags.Add(ExpenseCatalog.OtherTag); }
                    transferDescription = transferTags.SequenceEqual(new[] { ExpenseCatalog.OtherTag }) && !string.IsNullOrWhiteSpace(payment.Content)
                        ? payment.Content
                        : string.Join(", ", transferTags.Select(x => _translator.Translate(language, ExpenseCatalog.Categories.First(y => y.Tag == x).Name)));
                }
                transferTags.Add(PaidTag);
                response.Draft = new AiDraft
                {
                    Intent = transferIntent,
                    VehicleId = parsedContent.VehicleId,
                    Date = date.ToShortDateString(),
                    Mileage = parsedContent.Mileage ?? 0,
                    Cost = payment.Amount,
                    Description = transferDescription,
                    Tags = transferTags,
                    Notes = PaymentNote(payment, language),
                    Files = files,
                    AttachFiles = false
                };
                if (parsedContent.VehicleId == 0)
                {
                    response.Warnings.Add(_translator.Translate(language, "Which vehicle is this for?"));
                }
                response.IsDraft = true;
                return response;
            }
            //receipt
            decimal lineSum = lines.Sum(x => x.amount);
            if (total == 0 && lineSum > 0)
            {
                total = Math.Round(lineSum);
                response.Warnings.Add(_translator.Translate(language, "No total found, using the sum of the lines"));
            }
            else if (lineSum > 0 && Math.Abs(lineSum - total) > total * 0.01m)
            {
                response.Warnings.Add($"{_translator.Translate(language, "The lines do not add up to the total")}: {lineSum.ToString("C")} / {total.ToString("C")}");
            }
            if (total == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "No cost found"));
            }
            else if (total < 1000)
            {
                response.Warnings.Add(_translator.Translate(language, "The amount looks too small, please check"));
            }
            var vendor = Clean(image.vendor, 100);
            var itemText = string.Join(". ", lines.Select(x => Clean(x.name)));
            var parsedReceipt = VietnameseTextParser.Parse($"{Clean(image.plate, 20)} {vendor}. {itemText}", vehicles, today);
            var receiptIntent = parsedReceipt.Intent switch
            {
                AiIntent.Fuel => AiIntent.Fuel,
                AiIntent.Expense when parsedReceipt.ExpenseTags.Any() => AiIntent.Expense,
                _ => AiIntent.Service
            };
            var receiptTags = receiptIntent == AiIntent.Expense ? parsedReceipt.ExpenseTags.ToList() : (receiptIntent == AiIntent.Service ? parsedReceipt.MaintenanceTags.ToList() : new List<string>());
            string receiptDescription = string.Empty;
            if (receiptIntent == AiIntent.Service && receiptTags.Any())
            {
                receiptDescription = string.Join(", ", receiptTags.Select(x => _translator.Translate(language, MaintenanceCatalog.Items.First(y => y.Tag == x).Name)));
            }
            else if (receiptIntent == AiIntent.Expense)
            {
                receiptDescription = string.Join(", ", receiptTags.Select(x => _translator.Translate(language, ExpenseCatalog.Categories.First(y => y.Tag == x).Name)));
            }
            if (string.IsNullOrWhiteSpace(receiptDescription))
            {
                receiptDescription = Clean(lines.Any() ? string.Join(", ", lines.Take(4).Select(x => Clean(x.name, 40))) : vendor, 150);
            }
            var notes = new StringBuilder();
            notes.AppendLine($"{_translator.Translate(language, "Read from receipt")}{(string.IsNullOrWhiteSpace(vendor) ? "" : $": {vendor}")}");
            foreach (var line in lines.Take(30))
            {
                notes.AppendLine($"- {Clean(line.name, 80)}: {line.amount.ToString("C")}");
            }
            int receiptMileage = image.odometer >= 10 && image.odometer < 2000000 && image.odometer == Math.Floor(image.odometer) && !OdometerFromPlate(image.odometer, image.plate) ? (int)image.odometer : 0;
            response.Draft = new AiDraft
            {
                Intent = receiptIntent,
                VehicleId = parsedReceipt.VehicleId,
                Date = date.ToShortDateString(),
                Mileage = receiptMileage,
                Cost = total,
                FuelAmount = parsedReceipt.FuelAmount ?? 0,
                Description = receiptDescription,
                Tags = receiptTags,
                Notes = notes.ToString().TrimEnd(),
                Files = files,
                AttachFiles = true
            };
            if (parsedReceipt.VehicleId == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "Which vehicle is this for?"));
            }
            if (receiptIntent != AiIntent.Expense && receiptMileage == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "No odometer reading found"));
            }
            if (receiptIntent == AiIntent.Fuel && response.Draft.FuelAmount == 0)
            {
                response.Warnings.Add(_translator.Translate(language, "No fuel amount found"));
            }
            response.IsDraft = true;
            return response;
        }
        private List<UploadedFiles> StoreFiles(List<UploadedFiles>? files, bool attach)
        {
            var stored = new List<UploadedFiles>();
            foreach (var file in (files ?? new List<UploadedFiles>()).Where(x => IsValidTempLocation(x.Location)))
            {
                if (attach)
                {
                    stored.Add(new UploadedFiles { Name = Clean(Path.GetFileName(file.Name), 100), Location = _fileHelper.MoveFileFromTemp(file.Location, "documents/") });
                }
                else
                {
                    //the user chose not to keep the image.
                    _fileHelper.DeleteFile(file.Location);
                }
            }
            return stored;
        }
        public int GetRecordVehicleId(ImportMode recordType, int recordId)
        {
            return recordType switch
            {
                ImportMode.ServiceRecord => _serviceRecordDataAccess.GetServiceRecordById(recordId)?.VehicleId ?? 0,
                ImportMode.RepairRecord => _collisionRecordDataAccess.GetCollisionRecordById(recordId)?.VehicleId ?? 0,
                ImportMode.UpgradeRecord => _upgradeRecordDataAccess.GetUpgradeRecordById(recordId)?.VehicleId ?? 0,
                ImportMode.GasRecord => _gasRecordDataAccess.GetGasRecordById(recordId)?.VehicleId ?? 0,
                ImportMode.TaxRecord => _taxRecordDataAccess.GetTaxRecordById(recordId)?.VehicleId ?? 0,
                _ => 0
            };
        }
        /// <summary>
        /// Marks an existing record as paid by the transfer: adds the payment details to its notes, a "paid" tag and optionally the screenshot.
        /// </summary>
        public OperationResponse AttachPayment(AiAttachPaymentRequest request, UserConfig userConfig)
        {
            var language = userConfig.UserLanguage;
            var payment = request.Payment ?? new AiPayment();
            payment.Bank = Clean(payment.Bank, 60);
            payment.Reference = Clean(payment.Reference, 60);
            payment.Recipient = MaskAccountNumbers(Clean(payment.Recipient, 80));
            payment.Content = MaskAccountNumbers(Clean(payment.Content));
            payment.Date = Clean(payment.Date, 20);
            var note = PaymentNote(payment, language);
            string AddNote(string existing) => string.IsNullOrWhiteSpace(existing) ? note : $"{existing}\n{note}";
            List<string> AddTag(List<string> tags) => (tags ?? new List<string>()).Append(PaidTag).Distinct().ToList();
            bool AlreadyPaid(string notes) => (notes ?? string.Empty).Contains($"{ReferenceLabel}:");
            var alreadyPaid = OperationResponse.Failed(_translator.Translate(language, "This record already has a payment"));
            bool result;
            switch (request.RecordType)
            {
                case ImportMode.ServiceRecord:
                    var serviceRecord = _serviceRecordDataAccess.GetServiceRecordById(request.RecordId);
                    if (AlreadyPaid(serviceRecord.Notes)) { return alreadyPaid; }
                    serviceRecord.Notes = AddNote(serviceRecord.Notes); serviceRecord.Tags = AddTag(serviceRecord.Tags); serviceRecord.Files.AddRange(StoreFiles(request.Files, request.AttachFiles));
                    result = _serviceRecordDataAccess.SaveServiceRecordToVehicle(serviceRecord);
                    break;
                case ImportMode.RepairRecord:
                    var repairRecord = _collisionRecordDataAccess.GetCollisionRecordById(request.RecordId);
                    if (AlreadyPaid(repairRecord.Notes)) { return alreadyPaid; }
                    repairRecord.Notes = AddNote(repairRecord.Notes); repairRecord.Tags = AddTag(repairRecord.Tags); repairRecord.Files.AddRange(StoreFiles(request.Files, request.AttachFiles));
                    result = _collisionRecordDataAccess.SaveCollisionRecordToVehicle(repairRecord);
                    break;
                case ImportMode.UpgradeRecord:
                    var upgradeRecord = _upgradeRecordDataAccess.GetUpgradeRecordById(request.RecordId);
                    if (AlreadyPaid(upgradeRecord.Notes)) { return alreadyPaid; }
                    upgradeRecord.Notes = AddNote(upgradeRecord.Notes); upgradeRecord.Tags = AddTag(upgradeRecord.Tags); upgradeRecord.Files.AddRange(StoreFiles(request.Files, request.AttachFiles));
                    result = _upgradeRecordDataAccess.SaveUpgradeRecordToVehicle(upgradeRecord);
                    break;
                case ImportMode.GasRecord:
                    var gasRecord = _gasRecordDataAccess.GetGasRecordById(request.RecordId);
                    if (AlreadyPaid(gasRecord.Notes)) { return alreadyPaid; }
                    gasRecord.Notes = AddNote(gasRecord.Notes); gasRecord.Tags = AddTag(gasRecord.Tags); gasRecord.Files.AddRange(StoreFiles(request.Files, request.AttachFiles));
                    result = _gasRecordDataAccess.SaveGasRecordToVehicle(gasRecord);
                    break;
                case ImportMode.TaxRecord:
                    var taxRecord = _taxRecordDataAccess.GetTaxRecordById(request.RecordId);
                    if (AlreadyPaid(taxRecord.Notes)) { return alreadyPaid; }
                    taxRecord.Notes = AddNote(taxRecord.Notes); taxRecord.Tags = AddTag(taxRecord.Tags); taxRecord.Files.AddRange(StoreFiles(request.Files, request.AttachFiles));
                    result = _taxRecordDataAccess.SaveTaxRecordToVehicle(taxRecord);
                    break;
                default:
                    return OperationResponse.Failed(StaticHelper.GenericErrorMessage);
            }
            return OperationResponse.Conditional(result, _translator.Translate(language, "Saved"), StaticHelper.GenericErrorMessage);
        }
        public OperationResponse SaveDraft(AiDraft draft, Vehicle vehicle, UserConfig userConfig, int userId, string userName)
        {
            var language = userConfig.UserLanguage;
            if (!DateTime.TryParse(draft.Date, out DateTime date))
            {
                return OperationResponse.Failed(_translator.Translate(language, "Invalid date"));
            }
            if (draft.Cost < 0 || draft.Mileage < 0 || draft.FuelAmount < 0)
            {
                return OperationResponse.Failed(_translator.Translate(language, "Numbers cannot be negative"));
            }
            draft.Tags = (draft.Tags ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            draft.Description = (draft.Description ?? string.Empty).Trim();
            draft.Notes = draft.Notes ?? string.Empty;
            if (draft.Intent != AiIntent.Service && draft.Intent != AiIntent.Fuel && draft.Intent != AiIntent.Expense && draft.Intent != AiIntent.Odometer)
            {
                return OperationResponse.Failed(StaticHelper.GenericErrorMessage);
            }
            var storedFiles = StoreFiles(draft.Files, draft.AttachFiles);
            bool result;
            bool insertOdometer = false;
            ImportMode source = ImportMode.ServiceRecord;
            int recordId = 0;
            switch (draft.Intent)
            {
                case AiIntent.Service:
                    if (string.IsNullOrWhiteSpace(draft.Description))
                    {
                        return OperationResponse.Failed(_translator.Translate(language, "Description is required"));
                    }
                    var serviceRecord = new ServiceRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Cost = draft.Cost, Description = draft.Description, Notes = draft.Notes, Tags = draft.Tags, Files = storedFiles };
                    result = _serviceRecordDataAccess.SaveServiceRecordToVehicle(serviceRecord);
                    if (result)
                    {
                        _maintenanceLogic.PushbackRemindersByTags(vehicle.Id, serviceRecord.Tags, date, draft.Mileage > 0 ? draft.Mileage : null, new List<int>());
                        _eventLogic.PublishEvent(userId, WebHookPayload.FromGenericRecord(serviceRecord, "servicerecord.add", userName));
                        insertOdometer = true;
                        recordId = serviceRecord.Id;
                    }
                    break;
                case AiIntent.Fuel:
                    if (draft.FuelAmount <= 0)
                    {
                        return OperationResponse.Failed(_translator.Translate(language, "No fuel amount found"));
                    }
                    var gasRecord = new GasRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Gallons = draft.FuelAmount, Cost = draft.Cost, IsFillToFull = draft.IsFillToFull, Notes = draft.Notes, Tags = draft.Tags, Files = storedFiles };
                    result = _gasRecordDataAccess.SaveGasRecordToVehicle(gasRecord);
                    if (result)
                    {
                        _eventLogic.PublishEvent(userId, WebHookPayload.FromGasRecord(gasRecord, "gasrecord.add", userName));
                        insertOdometer = true;
                        source = ImportMode.GasRecord;
                        recordId = gasRecord.Id;
                        draft.Description = string.Empty;
                    }
                    break;
                case AiIntent.Expense:
                    if (string.IsNullOrWhiteSpace(draft.Description))
                    {
                        return OperationResponse.Failed(_translator.Translate(language, "Description is required"));
                    }
                    var taxRecord = new TaxRecord { VehicleId = vehicle.Id, Date = date, Cost = draft.Cost, Description = draft.Description, Notes = draft.Notes, Tags = draft.Tags, Files = storedFiles };
                    result = _taxRecordDataAccess.SaveTaxRecordToVehicle(taxRecord);
                    if (result)
                    {
                        _maintenanceLogic.PushbackRemindersByTags(vehicle.Id, taxRecord.Tags, date, null, new List<int>());
                        _eventLogic.PublishEvent(userId, WebHookPayload.FromTaxRecord(taxRecord, "taxrecord.add", userName));
                    }
                    break;
                case AiIntent.Odometer:
                    if (draft.Mileage <= 0)
                    {
                        return OperationResponse.Failed(_translator.Translate(language, "No odometer reading found"));
                    }
                    result = _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Notes = draft.Notes, Files = storedFiles });
                    break;
                default:
                    return OperationResponse.Failed(StaticHelper.GenericErrorMessage);
            }
            if (result && insertOdometer && draft.Mileage > 0 && userConfig.EnableAutoOdometerInsert)
            {
                _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord
                {
                    VehicleId = vehicle.Id,
                    Date = date,
                    Mileage = draft.Mileage,
                    Notes = $"{_translator.Translate(language, StaticHelper.GetAutoInsertVerbiage(source, false))}: {draft.Description}".TrimEnd(' ', ':'),
                    Files = StaticHelper.CreateAttachmentFromRecord(source, recordId, draft.Description)
                });
            }
            return OperationResponse.Conditional(result, _translator.Translate(language, "Saved"), StaticHelper.GenericErrorMessage);
        }
    }
}
