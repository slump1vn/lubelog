namespace CarCareTracker.Models
{
    /// <summary>
    /// The read-only things the assistant can look up. The model (or the keyword rules) only picks one
    /// of these and its arguments; the numbers are always computed by code from the stored data.
    /// </summary>
    public enum AssistantTool
    {
        Unknown = 0,
        /// <summary>Joint account balance and this cycle's money in and out.</summary>
        Balance = 1,
        /// <summary>Money in and out for a budget cycle, with the biggest spending categories.</summary>
        CycleSummary = 2,
        /// <summary>Spending of one budget category against its monthly limit.</summary>
        Category = 3,
        /// <summary>Paydays and debt payments coming up.</summary>
        Upcoming = 4,
        /// <summary>The latest transactions of the joint account.</summary>
        Recent = 5,
        /// <summary>Transactions whose content or note contains a word.</summary>
        Search = 6,
        /// <summary>Vehicle log entries and vehicle questions, handled by the existing vehicle assistant.</summary>
        Vehicle = 7,
        /// <summary>What the assistant can do.</summary>
        Help = 8
    }
    public enum AssistantPeriod
    {
        /// <summary>The budget cycle that contains today (e.g. 5th to 4th).</summary>
        Current = 0,
        /// <summary>The cycle before the current one.</summary>
        Previous = 1
    }
    /// <summary>A tool picked for one message, with its checked arguments.</summary>
    public class AssistantRoute
    {
        public AssistantTool Tool { get; set; } = AssistantTool.Unknown;
        public AssistantPeriod Period { get; set; } = AssistantPeriod.Current;
        /// <summary>Budget category key, for <see cref="AssistantTool.Category"/>.</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>Word to look for, for <see cref="AssistantTool.Search"/>.</summary>
        public string Keyword { get; set; } = string.Empty;
        /// <summary>Only money in or only money out; null for both.</summary>
        public TransferDirection? Direction { get; set; }
        public int Count { get; set; } = 5;
        /// <summary>"rules" when the keyword rules decided, "ai" when the home AI did.</summary>
        public string Source { get; set; } = "rules";
    }
    /// <summary>One labelled number or line in an answer, e.g. "Balance" = "12,500,000 ₫".</summary>
    public class AssistantFact
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
    /// <summary>
    /// One entry of a user's chat history, kept on the server for <see cref="Logic.AssistantRules.RetentionDays"/> days.
    /// Photos are never stored, only a note that one was sent.
    /// </summary>
    public class AssistantMessage
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; } = "user";
        public string Text { get; set; } = string.Empty;
        /// <summary>"text", "photo", "answer", "draft", "payment", "food", "help" or "error".</summary>
        public string Kind { get; set; } = "text";
        public List<AssistantFact> Facts { get; set; } = new List<AssistantFact>();
        public DateTime CreatedAtUtc { get; set; }
    }
    /// <summary>The assistant's answer to one message or photo.</summary>
    public class AssistantReply
    {
        /// <summary>Id of the stored answer in the history; 0 when it could not be stored.</summary>
        public int MessageId { get; set; }
        /// <summary>"answer", "draft", "payment", "food", "help" or "error".</summary>
        public string Kind { get; set; } = "answer";
        public string Text { get; set; } = string.Empty;
        public List<AssistantFact> Facts { get; set; } = new List<AssistantFact>();
        /// <summary>Follow-up questions the user can tap.</summary>
        public List<string> Suggestions { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        /// <summary>The tool that answered, e.g. "balance"; empty when none.</summary>
        public string Tool { get; set; } = string.Empty;
        /// <summary>"rules" or "ai".</summary>
        public string Source { get; set; } = "rules";
        /// <summary>A vehicle record to check and save (kind "draft"). Nothing is saved until the user confirms.</summary>
        public AiDraft? Draft { get; set; }
        /// <summary>A bank transfer read from a screenshot (kind "payment").</summary>
        public AiPayment? Payment { get; set; }
        public List<AiPaymentMatch> Matches { get; set; } = new List<AiPaymentMatch>();
        /// <summary>A food label read by the AI and rated by the rules, not saved (kind "food"). The user should check the numbers.</summary>
        public FoodAnalyzeResult? Food { get; set; }
    }
    public class AssistantHistoryPage
    {
        /// <summary>Oldest first, so it can be shown top to bottom.</summary>
        public List<AssistantMessage> Items { get; set; } = new List<AssistantMessage>();
        /// <summary>Pass as "before" to get older messages; null when there are none.</summary>
        public int? NextBefore { get; set; }
    }
    public class AssistantChatRequest
    {
        public string Text { get; set; } = string.Empty;
    }
    public class AssistantPanelViewModel
    {
        public List<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
        public bool ModelConfigured { get; set; }
        public List<string> Suggestions { get; set; } = new List<string>();
    }
}
