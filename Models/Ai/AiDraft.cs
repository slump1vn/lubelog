namespace CarCareTracker.Models
{
    public enum AiIntent
    {
        Unknown = 0,
        Service = 1,
        Fuel = 2,
        Expense = 3,
        Odometer = 4,
        QueryReminders = 5,
        QuerySpending = 6
    }
    /// <summary>
    /// A record proposed from a chat message. Nothing is saved until the user confirms it.
    /// </summary>
    public class AiDraft
    {
        public AiIntent Intent { get; set; } = AiIntent.Unknown;
        public int VehicleId { get; set; }
        /// <summary>
        /// Short date string in the server culture, same as other record inputs.
        /// </summary>
        public string Date { get; set; } = string.Empty;
        public int Mileage { get; set; }
        public decimal Cost { get; set; }
        /// <summary>
        /// Fuel amount in the user's fuel unit.
        /// </summary>
        public decimal FuelAmount { get; set; }
        public bool IsFillToFull { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public string Notes { get; set; } = string.Empty;
        /// <summary>
        /// Uploaded image the draft was read from; attached to the record when AttachFiles is set.
        /// </summary>
        public List<UploadedFiles> Files { get; set; } = new List<UploadedFiles>();
        public bool AttachFiles { get; set; }
    }
    /// <summary>
    /// A bank transfer read from a screenshot. The account number is never requested or stored.
    /// </summary>
    public class AiPayment
    {
        public decimal Amount { get; set; }
        /// <summary>
        /// Amount formatted as currency in the server culture, for display only.
        /// </summary>
        public string AmountText { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Bank { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
    /// <summary>
    /// An existing unpaid-looking record with the same amount as a transfer.
    /// </summary>
    public class AiPaymentMatch
    {
        public ImportMode RecordType { get; set; }
        public int RecordId { get; set; }
        public int VehicleId { get; set; }
        public string VehicleName { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public string CostText { get; set; } = string.Empty;
    }
    public class AiAttachPaymentRequest
    {
        public ImportMode RecordType { get; set; }
        public int RecordId { get; set; }
        public AiPayment Payment { get; set; } = new AiPayment();
        public List<UploadedFiles> Files { get; set; } = new List<UploadedFiles>();
        public bool AttachFiles { get; set; }
    }
    public class AiChatResponse
    {
        /// <summary>
        /// True when the message describes a record that can be saved after confirmation.
        /// </summary>
        public bool IsDraft { get; set; }
        public AiDraft Draft { get; set; } = new AiDraft();
        /// <summary>
        /// Answer to a question, or a note about what could not be understood.
        /// </summary>
        public string Reply { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new List<string>();
        /// <summary>
        /// "ai" when the local model was used, "rules" when only the built-in parser was used.
        /// </summary>
        public string Source { get; set; } = "rules";
        /// <summary>
        /// Set when the message was a bank transfer screenshot.
        /// </summary>
        public AiPayment? Payment { get; set; }
        public List<AiPaymentMatch> Matches { get; set; } = new List<AiPaymentMatch>();
    }
}
