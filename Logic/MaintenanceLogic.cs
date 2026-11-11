using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;

namespace CarCareTracker.Logic
{
    public interface IMaintenanceLogic
    {
        int ApplyDefaultSchedule(Vehicle vehicle, string userLanguage);
        int PushbackRemindersByTags(int vehicleId, List<string> tags, DateTime date, int? mileage, List<int> excludedReminderIds);
    }
    public class MaintenanceLogic : IMaintenanceLogic
    {
        private readonly IReminderRecordDataAccess _reminderRecordDataAccess;
        private readonly IServiceRecordDataAccess _serviceRecordDataAccess;
        private readonly IVehicleLogic _vehicleLogic;
        private readonly IReminderHelper _reminderHelper;
        private readonly ITranslationHelper _translator;
        private readonly ILogger<MaintenanceLogic> _logger;
        public MaintenanceLogic(IReminderRecordDataAccess reminderRecordDataAccess,
            IServiceRecordDataAccess serviceRecordDataAccess,
            IVehicleLogic vehicleLogic,
            IReminderHelper reminderHelper,
            ITranslationHelper translator,
            ILogger<MaintenanceLogic> logger)
        {
            _reminderRecordDataAccess = reminderRecordDataAccess;
            _serviceRecordDataAccess = serviceRecordDataAccess;
            _vehicleLogic = vehicleLogic;
            _reminderHelper = reminderHelper;
            _translator = translator;
            _logger = logger;
        }
        private static bool HasTag(List<string> tags, string tag)
        {
            return tags != null && tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>
        /// Creates one recurring reminder per default maintenance item that the vehicle does not have yet.
        /// The next due point is counted from the last service record carrying the item's tag,
        /// or from today and the current odometer when the item has never been recorded.
        /// </summary>
        /// <returns>Number of reminders created.</returns>
        public int ApplyDefaultSchedule(Vehicle vehicle, string userLanguage)
        {
            var existingReminders = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(vehicle.Id);
            var serviceRecords = _serviceRecordDataAccess.GetServiceRecordsByVehicleId(vehicle.Id);
            var currentMileage = _vehicleLogic.GetMaxMileage(vehicle.Id);
            int created = 0;
            foreach (var item in MaintenanceCatalog.GetItemsForVehicle(vehicle))
            {
                if (existingReminders.Any(x => HasTag(x.Tags, item.Tag)))
                {
                    continue;
                }
                var interval = item.Intervals[vehicle.VehicleCategory];
                var lastService = serviceRecords.Where(x => HasTag(x.Tags, item.Tag)).OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).FirstOrDefault();
                var baseDate = lastService?.Date.Date ?? DateTime.Now.Date;
                var baseMileage = lastService?.Mileage ?? currentMileage;
                //vehicles that track hours or have no odometer can only be reminded by date.
                bool useMileage = interval.Kilometers > 0 && !vehicle.UseHours && !vehicle.OdometerOptional;
                bool useDate = interval.Months > 0;
                if (!useMileage && !useDate)
                {
                    continue;
                }
                var reminder = new ReminderRecord
                {
                    VehicleId = vehicle.Id,
                    Description = _translator.Translate(userLanguage, item.Name),
                    IsRecurring = true,
                    Metric = useMileage && useDate ? ReminderMetric.Both : (useMileage ? ReminderMetric.Odometer : ReminderMetric.Date),
                    ReminderMileageInterval = ReminderMileageInterval.Other,
                    CustomMileageInterval = useMileage ? interval.Kilometers : 0,
                    ReminderMonthInterval = ReminderMonthInterval.Other,
                    CustomMonthInterval = useDate ? interval.Months : 0,
                    CustomMonthIntervalUnit = ReminderIntervalUnit.Months,
                    Date = useDate ? baseDate.AddMonths(interval.Months) : baseDate,
                    Mileage = useMileage ? baseMileage + interval.Kilometers : 0,
                    Tags = new List<string> { item.Tag }
                };
                if (_reminderRecordDataAccess.SaveReminderRecordToVehicle(reminder))
                {
                    created++;
                }
            }
            return created;
        }
        /// <summary>
        /// Pushes back every recurring reminder of the vehicle that shares a maintenance tag with a newly saved record.
        /// </summary>
        /// <returns>Number of reminders updated.</returns>
        public int PushbackRemindersByTags(int vehicleId, List<string> tags, DateTime date, int? mileage, List<int> excludedReminderIds)
        {
            var maintenanceTags = (tags ?? new List<string>()).Where(MaintenanceCatalog.IsMaintenanceTag).ToList();
            if (!maintenanceTags.Any())
            {
                return 0;
            }
            int updated = 0;
            try
            {
                var reminders = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(vehicleId)
                    .Where(x => x.IsRecurring && !(excludedReminderIds ?? new List<int>()).Contains(x.Id) && maintenanceTags.Any(y => HasTag(x.Tags, y)));
                foreach (var reminder in reminders)
                {
                    var updatedReminder = _reminderHelper.GetUpdatedRecurringReminderRecord(reminder, date, mileage);
                    if (_reminderRecordDataAccess.SaveReminderRecordToVehicle(updatedReminder))
                    {
                        updated++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
            return updated;
        }
    }
}
