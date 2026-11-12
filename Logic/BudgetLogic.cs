using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using System.Text.Json;

namespace CarCareTracker.Logic
{
    public interface IBudgetLogic
    {
        int GetHouseholdId(int userId);
        List<(int UserId, string Name)> GetHouseholdMembers(int householdId);
        BudgetProfile? GetProfile(int userId);
        /// <summary>Validates and saves the profile for the user's household, recording the change.</summary>
        (bool Success, List<BudgetIssue> Issues) SaveProfile(int userId, BudgetProfile profile);
        BudgetProfile CreateTemplate(int userId);
        List<BudgetAuditEntry> GetRecentChanges(int userId, int limit = 20);
    }
    public class BudgetLogic : IBudgetLogic
    {
        private readonly IBudgetDataAccess _budgetData;
        private readonly IUserHouseholdDataAccess _householdData;
        private readonly IUserRecordDataAccess _userData;
        private readonly bool _sharedHousehold;
        public BudgetLogic(IBudgetDataAccess budgetData, IUserHouseholdDataAccess householdData, IUserRecordDataAccess userData, IConfiguration configuration)
        {
            _sharedHousehold = CarCareTracker.Helper.StaticHelper.CheckConfigBoolean(configuration, HouseholdRules.SharedHouseholdSetting);
            _budgetData = budgetData;
            _householdData = householdData;
            _userData = userData;
        }
        /// <summary>
        /// The household a user belongs to: the head's user id. With LUBELOGGER_SHARED_HOUSEHOLD on, everyone
        /// shares the admin's household (-1). Otherwise a user who is a member of another user's household
        /// uses that household (the lowest head id when there are several); everyone else is the head of their own.
        /// </summary>
        public int GetHouseholdId(int userId)
        {
            if (_sharedHousehold)
            {
                return HouseholdRules.SharedHouseholdId;
            }
            return HouseholdRules.Resolve(userId, false, _householdData.GetUserHouseholdByChildUserId(userId).Select(x => x.Id.ParentUserId));
        }
        public List<(int UserId, string Name)> GetHouseholdMembers(int householdId)
        {
            var members = new List<(int UserId, string Name)> { (householdId, GetUserName(householdId)) };
            if (_sharedHousehold && householdId == HouseholdRules.SharedHouseholdId)
            {
                //everyone who can sign in belongs to the family household.
                members.AddRange(_userData.GetUsers().Where(x => x.Id > 0).OrderBy(x => x.Id).Select(x => (x.Id, string.IsNullOrWhiteSpace(x.UserName) ? x.Id.ToString() : x.UserName)));
                return members;
            }
            foreach (var member in _householdData.GetUserHouseholdByParentUserId(householdId).OrderBy(x => x.Id.ChildUserId))
            {
                members.Add((member.Id.ChildUserId, GetUserName(member.Id.ChildUserId)));
            }
            return members;
        }
        public BudgetProfile? GetProfile(int userId)
        {
            return _budgetData.GetBudgetProfile(GetHouseholdId(userId));
        }
        public (bool Success, List<BudgetIssue> Issues) SaveProfile(int userId, BudgetProfile profile)
        {
            var householdId = GetHouseholdId(userId);
            //never trust the id sent by the browser: a profile always belongs to the caller's household.
            profile.Id = householdId;
            var memberIds = GetHouseholdMembers(householdId).Select(x => x.UserId).ToHashSet();
            var issues = BudgetRules.Validate(profile);
            if (profile.Funds.Any(x => x.Kind == FundKind.Personal && x.OwnerUserId != 0 && !memberIds.Contains(x.OwnerUserId)))
            {
                issues.Add(new BudgetIssue("A personal fund must belong to a member of the household"));
            }
            if (issues.Any())
            {
                return (false, issues);
            }
            var before = _budgetData.GetBudgetProfile(householdId);
            profile.UpdatedAtUtc = DateTime.UtcNow;
            profile.UpdatedByUserId = userId;
            if (!_budgetData.SaveBudgetProfile(profile))
            {
                return (false, new List<BudgetIssue> { new BudgetIssue("The budget could not be saved") });
            }
            _budgetData.AddAuditEntry(new BudgetAuditEntry
            {
                HouseholdId = householdId,
                UserId = userId,
                TimestampUtc = profile.UpdatedAtUtc,
                Entity = nameof(BudgetProfile),
                Action = before == null ? "Create" : "Update",
                Before = before == null ? string.Empty : JsonSerializer.Serialize(before),
                After = JsonSerializer.Serialize(profile)
            });
            return (true, new List<BudgetIssue>());
        }
        public BudgetProfile CreateTemplate(int userId)
        {
            var householdId = GetHouseholdId(userId);
            return BudgetRules.CreateFamilyTemplate(householdId, GetHouseholdMembers(householdId));
        }
        public List<BudgetAuditEntry> GetRecentChanges(int userId, int limit = 20)
        {
            return _budgetData.GetAuditEntries(GetHouseholdId(userId), limit);
        }
        private string GetUserName(int userId)
        {
            if (userId <= 0)
            {
                return "admin";
            }
            var user = _userData.GetUserRecordById(userId);
            return string.IsNullOrWhiteSpace(user?.UserName) ? userId.ToString() : user.UserName;
        }
    }
}
