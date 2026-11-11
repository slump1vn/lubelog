namespace CarCareTracker.Models
{
    public class HouseholdExpenseRow
    {
        public int VehicleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public decimal Service { get; set; }
        public decimal Repair { get; set; }
        public decimal Upgrade { get; set; }
        public decimal Fuel { get; set; }
        public decimal Other { get; set; }
        public decimal Total { get { return Service + Repair + Upgrade + Fuel + Other; } }
    }
    /// <summary>
    /// Costs of every vehicle the user can access, for one year.
    /// </summary>
    public class HouseholdExpenseViewModel
    {
        public int Year { get; set; }
        public List<int> Years { get; set; } = new List<int>();
        public List<HouseholdExpenseRow> Vehicles { get; set; } = new List<HouseholdExpenseRow>();
        public List<HouseholdExpenseRow> Months { get; set; } = new List<HouseholdExpenseRow>();
        /// <summary>
        /// Key is the English category name (translation key).
        /// </summary>
        public Dictionary<string, decimal> OtherByCategory { get; set; } = new Dictionary<string, decimal>();
    }
}
