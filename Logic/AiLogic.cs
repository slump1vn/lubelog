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
        private readonly ILogger<AiLogic> _logger;
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
            _logger = logger;
        }
        private string ModelUrl => (_configuration["LUBELOGGER_AI_URL"] ?? string.Empty).TrimEnd('/');
        private string ModelName => _configuration["LUBELOGGER_AI_MODEL"] ?? "qwen2.5:7b";
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
        private async Task<ModelResult?> AskModel(string message, List<Vehicle> vehicles, DateTime today)
        {
            var request = new JsonObject
            {
                ["model"] = ModelName,
                ["stream"] = false,
                ["format"] = BuildSchema(),
                ["options"] = new JsonObject { ["temperature"] = 0 },
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = BuildSystemPrompt(vehicles, today) },
                    new JsonObject { ["role"] = "user", ["content"] = message })
            };
            await _modelLock.WaitAsync();
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(int.TryParse(_configuration["LUBELOGGER_AI_TIMEOUT"], out int timeout) ? timeout : 180);
                var response = await client.PostAsync($"{ModelUrl}/api/chat", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                var content = body?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
                return JsonSerializer.Deserialize<ModelResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"AI model request failed, falling back to rules: {ex.Message}");
                return null;
            }
            finally
            {
                _modelLock.Release();
            }
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
                    var serviceRecord = new ServiceRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Cost = draft.Cost, Description = draft.Description, Notes = draft.Notes, Tags = draft.Tags };
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
                    var gasRecord = new GasRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Gallons = draft.FuelAmount, Cost = draft.Cost, IsFillToFull = draft.IsFillToFull, Notes = draft.Notes, Tags = draft.Tags };
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
                    var taxRecord = new TaxRecord { VehicleId = vehicle.Id, Date = date, Cost = draft.Cost, Description = draft.Description, Notes = draft.Notes, Tags = draft.Tags };
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
                    result = _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord { VehicleId = vehicle.Id, Date = date, Mileage = draft.Mileage, Notes = draft.Notes });
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
