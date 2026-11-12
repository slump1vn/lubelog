namespace CarCareTracker.Logic
{
    /// <summary>
    /// Which household's finances a user sees. A family install keeps one shared household: every
    /// account that signs in sees and edits the same budget, joint account and transactions (each change
    /// is still recorded with who made it). Otherwise the vehicle "household" links decide it.
    /// </summary>
    public static class HouseholdRules
    {
        /// <summary>Setting that turns on the single shared household (on in docker-compose.family.yml).</summary>
        public const string SharedHouseholdSetting = "LUBELOGGER_SHARED_HOUSEHOLD";
        /// <summary>The shared household belongs to the root (admin) account, id -1, where the data already lives.</summary>
        public const int SharedHouseholdId = -1;

        public static int Resolve(int userId, bool sharedHousehold, IEnumerable<int> headsOfHouseholdsUserBelongsTo)
        {
            if (sharedHousehold)
            {
                return SharedHouseholdId;
            }
            var heads = headsOfHouseholdsUserBelongsTo.ToList();
            return heads.Any() ? heads.Min() : userId;
        }
    }
}
