using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CarCareTracker.Logic
{
    /// <summary>What the vision model returns for a household receipt or a bank transfer screenshot.</summary>
    public class ReceiptModelAnswer
    {
        public string kind { get; set; } = "other";
        public string vendor { get; set; } = string.Empty;
        public string date { get; set; } = string.Empty;
        public decimal total { get; set; }
        public string reference { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }
    public class ReceiptReadResult
    {
        public ReceiptReadingDto? Reading { get; set; }
        /// <summary>English message when nothing could be read; empty on success.</summary>
        public string Message { get; set; } = string.Empty;
    }
    public interface IReceiptLogic
    {
        bool ModelConfigured { get; }
        /// <summary>Reads one photo with the home AI and finds the balance gaps the payment may explain. The photo is not stored.</summary>
        Task<ReceiptReadResult> Read(int householdId, byte[] image);
    }

    /// <summary>
    /// Receipts and transfer screenshots for the joint account. The AI only reads the photo; matching it to
    /// the bank data is done by FinanceApiRules, and the family confirms before anything is written
    /// (the app then explains the chosen gap with a manual entry).
    /// </summary>
    public class ReceiptLogic : IReceiptLogic
    {
        private readonly IAiLogic _aiLogic;
        private readonly IBankTransactionDataAccess _transactionData;
        public ReceiptLogic(IAiLogic aiLogic, IBankTransactionDataAccess transactionData)
        {
            _aiLogic = aiLogic;
            _transactionData = transactionData;
        }
        public bool ModelConfigured => _aiLogic.ModelConfigured;

        public async Task<ReceiptReadResult> Read(int householdId, byte[] image)
        {
            if (!ModelConfigured)
            {
                return new ReceiptReadResult { Message = "Reading images needs the home AI, which is not turned on" };
            }
            var today = DateOnly.FromDateTime(DateTime.UtcNow + SepayRules.VietnamOffset);
            var answer = await _aiLogic.AskVision<ReceiptModelAnswer>(ReceiptRules.Prompt(today), "Đọc ảnh này.", ReceiptRules.Schema(), Convert.ToBase64String(image));
            if (answer == null)
            {
                return new ReceiptReadResult { Message = "The home AI did not answer. Please try again later" };
            }
            var reading = ReceiptRules.ToReading(answer, today);
            if (reading.Kind == "other")
            {
                return new ReceiptReadResult { Reading = reading, Message = "This does not look like a receipt or a bank transfer" };
            }
            var all = _transactionData.GetSince(householdId, SepayLogic.HistoryStartUtc);
            var recorded = FinanceApiRules.FindRecorded(all, reading.Total, reading.Date, reading.Reference);
            if (recorded != null)
            {
                reading.AlreadyRecorded = FinanceApiRules.ToDto(recorded);
            }
            reading.Gaps = FinanceApiRules.MatchGaps(ReconciliationRules.FindGaps(all), reading.Total, reading.Date).Select(FinanceApiRules.ToDto).ToList();
            return new ReceiptReadResult { Reading = reading };
        }
    }

    public static class ReceiptRules
    {
        public static string Prompt(DateOnly today)
        {
            return string.Join("\n",
                "Bạn đọc ảnh chụp hoá đơn mua hàng hoặc ảnh màn hình chuyển khoản của một gia đình Việt Nam và trả về JSON đúng theo schema. Không thêm chữ nào ngoài JSON.",
                //only the year: given the full date, models copy it into receipts that show no date.
                $"Năm hiện tại là {today:yyyy}; chỉ dùng để đoán năm khi chứng từ ghi ngày/tháng mà thiếu năm.",
                "kind: receipt = hoá đơn, biên lai, phiếu thanh toán của cửa hàng, siêu thị, nhà hàng, điện nước...; transfer = ảnh màn hình chuyển khoản thành công của ngân hàng hoặc ví điện tử; other = không phải hai loại trên hoặc không đọc được.",
                "Mọi số tiền là số nguyên tính bằng đồng, không có dấu phân cách: 1.200.000đ = 1200000.",
                "date: ngày in trên chứng từ, dạng yyyy-MM-dd; chuỗi rỗng nếu không thấy. Không được tự đoán ngày.",
                "total: tổng tiền phải trả (đã gồm VAT), hoặc số tiền chuyển.",
                "vendor: tên cửa hàng; với transfer là TÊN người nhận.",
                "reference: với transfer là giá trị cạnh nhãn \"Mã giao dịch\" hoặc \"Mã GD\"; với receipt là chuỗi rỗng.",
                "content: với transfer là nội dung chuyển khoản; với receipt là tên vài món chính, cách nhau bởi dấu phẩy.",
                "Trường nào không có trên ảnh thì để chuỗi rỗng hoặc 0. KHÔNG ghi số tài khoản hay số thẻ vào bất kỳ trường nào.");
        }
        public static JsonObject Schema()
        {
            JsonObject Text() => new JsonObject { ["type"] = "string" };
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("receipt", "transfer", "other") },
                    ["vendor"] = Text(), ["date"] = Text(), ["total"] = new JsonObject { ["type"] = "number" }, ["reference"] = Text(), ["content"] = Text()
                },
                ["required"] = new JsonArray("kind", "vendor", "date", "total", "reference", "content")
            };
        }
        /// <summary>Cleans the model's answer: known kinds only, a plausible date, whole positive VND, no account numbers.</summary>
        public static ReceiptReadingDto ToReading(ReceiptModelAnswer answer, DateOnly today)
        {
            var kind = answer.kind is "receipt" or "transfer" ? answer.kind : "other";
            DateOnly? date = DateOnly.TryParseExact(answer.date ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                && parsed <= today.AddDays(1) && parsed > today.AddYears(-5) ? parsed : null;
            var total = answer.total > 0 && answer.total < 100_000_000_000m ? (long)Math.Round(answer.total) : 0;
            return new ReceiptReadingDto
            {
                Kind = kind,
                Vendor = Mask(Clean(answer.vendor, 80)),
                Date = date,
                Total = total,
                Reference = kind == "transfer" ? Clean(answer.reference, 60) : string.Empty,
                Content = Mask(Clean(answer.content, 200))
            };
        }
        private static string Clean(string? value, int maxLength)
        {
            var cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
        }
        /// <summary>Long digit runs are most likely account or card numbers: keep the last four digits only.</summary>
        public static string Mask(string value)
        {
            return Regex.Replace(value, @"\d{8,}", m => "****" + m.Value[^4..]);
        }
    }
}
