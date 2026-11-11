namespace CarCareTracker.Models
{
    /// <summary>
    /// A maintenance item and its default interval for one vehicle category.
    /// </summary>
    public class MaintenanceInterval
    {
        public int Kilometers { get; set; }
        public int Months { get; set; }
    }
    public class MaintenanceItem
    {
        /// <summary>
        /// Tag (no spaces) used to link service records with reminders.
        /// </summary>
        public string Tag { get; set; } = string.Empty;
        /// <summary>
        /// English name, also used as the translation key.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Not applicable to electric vehicles.
        /// </summary>
        public bool CombustionOnly { get; set; }
        /// <summary>
        /// Not applicable to diesel vehicles.
        /// </summary>
        public bool SkipDiesel { get; set; }
        public Dictionary<VehicleCategory, MaintenanceInterval> Intervals { get; set; } = new Dictionary<VehicleCategory, MaintenanceInterval>();
    }
    /// <summary>
    /// Default maintenance schedules for cars, motorbikes and scooters.
    /// Intervals are conservative defaults; users adjust them by editing the generated reminders.
    /// </summary>
    public static class MaintenanceCatalog
    {
        private static MaintenanceItem Item(string tag, string name, (int km, int months)? car, (int km, int months)? motorbike, (int km, int months)? scooter, bool combustionOnly = false, bool skipDiesel = false)
        {
            var item = new MaintenanceItem { Tag = tag, Name = name, CombustionOnly = combustionOnly, SkipDiesel = skipDiesel };
            if (car.HasValue) { item.Intervals[VehicleCategory.Car] = new MaintenanceInterval { Kilometers = car.Value.km, Months = car.Value.months }; }
            if (motorbike.HasValue) { item.Intervals[VehicleCategory.Motorbike] = new MaintenanceInterval { Kilometers = motorbike.Value.km, Months = motorbike.Value.months }; }
            if (scooter.HasValue) { item.Intervals[VehicleCategory.Scooter] = new MaintenanceInterval { Kilometers = scooter.Value.km, Months = scooter.Value.months }; }
            return item;
        }
        public static readonly List<MaintenanceItem> Items = new List<MaintenanceItem>
        {
            Item("dau-may", "Engine Oil Change", (5000, 6), (2000, 3), (2000, 3), combustionOnly: true),
            Item("loc-dau", "Oil Filter Replacement", (10000, 12), (6000, 12), (6000, 12), combustionOnly: true),
            Item("dau-hop-so", "Transmission Fluid Change", (50000, 48), null, null, combustionOnly: true),
            Item("nhot-lap", "Final Drive Oil Change", null, null, (6000, 12), combustionOnly: true),
            Item("nuoc-lam-mat", "Coolant Change", (40000, 24), (20000, 24), (20000, 24)),
            Item("ac-quy", "Battery Check/Replacement", (0, 24), (0, 24), (0, 24)),
            Item("dao-lop", "Tire Rotation", (10000, 12), null, null),
            Item("lop", "Tire Replacement", (45000, 60), (18000, 36), (18000, 36)),
            Item("loc-gio", "Engine Air Filter Replacement", (20000, 24), (10000, 12), (10000, 12), combustionOnly: true),
            Item("loc-gio-dieu-hoa", "Cabin Air Filter Replacement", (10000, 12), null, null),
            Item("ma-phanh", "Brake Pads Check/Replacement", (30000, 24), (10000, 12), (10000, 12)),
            Item("dau-phanh", "Brake Fluid Change", (40000, 24), (20000, 24), (20000, 24)),
            Item("bugi", "Spark Plug Replacement", (40000, 48), (10000, 24), (10000, 24), combustionOnly: true, skipDiesel: true),
            Item("nhong-xich", "Chain and Sprockets Replacement", null, (20000, 36), null),
            Item("day-curoa", "Drive Belt Replacement", null, null, (20000, 36), combustionOnly: true),
            Item("bao-duong-dinh-ky", "Full Periodic Maintenance", (10000, 12), (4000, 6), (4000, 6)),
            Item("dang-kiem", "Vehicle Inspection Renewal", (0, 12), null, null),
            Item("bao-hiem", "Compulsory Insurance Renewal", (0, 12), (0, 12), (0, 12))
        };
        public static List<MaintenanceItem> GetItemsForVehicle(Vehicle vehicle)
        {
            return Items.Where(x => x.Intervals.ContainsKey(vehicle.VehicleCategory)
                && !(x.CombustionOnly && vehicle.IsElectric)
                && !(x.SkipDiesel && vehicle.IsDiesel)).ToList();
        }
        public static bool IsMaintenanceTag(string tag)
        {
            return Items.Any(x => string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));
        }
    }
}
