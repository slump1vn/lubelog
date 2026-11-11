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
    }
}
