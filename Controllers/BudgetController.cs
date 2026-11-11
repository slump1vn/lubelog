using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// Household budget setup: incomes, paydays, funds, limits and debts.
    /// Writes take JSON bodies only, so a cross-site form cannot submit them.
    /// </summary>
    [Authorize]
    public class BudgetController : Controller
    {
        private readonly IBudgetLogic _budgetLogic;
        private readonly IConfigHelper _config;
        private readonly ITranslationHelper _translator;
        private readonly ISepayLogic _sepayLogic;
        public BudgetController(IBudgetLogic budgetLogic, IConfigHelper config, ITranslationHelper translator, ISepayLogic sepayLogic)
        {
            _budgetLogic = budgetLogic;
            _sepayLogic = sepayLogic;
            _config = config;
            _translator = translator;
        }
        private int GetUserID()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        }
        [HttpGet]
        public IActionResult Settings(bool template = false)
        {
            var userId = GetUserID();
            var householdId = _budgetLogic.GetHouseholdId(userId);
            var profile = _budgetLogic.GetProfile(userId);
            var viewModel = new BudgetSettingsViewModel
            {
                HasProfile = profile != null,
                IsTemplate = template,
                Profile = template ? _budgetLogic.CreateTemplate(userId) : profile ?? new BudgetProfile { Id = householdId },
                IsHouseholdHead = householdId == userId,
                RecentChanges = _budgetLogic.GetRecentChanges(userId),
                UserNames = _budgetLogic.GetHouseholdMembers(householdId).ToDictionary(x => x.UserId, x => x.Name)
            };
            if (profile != null && !template)
            {
                viewModel.Errors = BudgetRules.Validate(profile).Select(Localize).ToList();
            }
            return PartialView("_Settings", viewModel);
        }
        [HttpGet]
        public IActionResult Profile()
        {
            var userId = GetUserID();
            return Json(_budgetLogic.GetProfile(userId) ?? new BudgetProfile { Id = _budgetLogic.GetHouseholdId(userId) });
        }
        [HttpPost]
        public IActionResult Save([FromBody] BudgetProfile profile)
        {
            if (profile == null)
            {
                return Json(OperationResponse.Failed());
            }
            var result = _budgetLogic.SaveProfile(GetUserID(), profile);
            if (!result.Success)
            {
                return Json(OperationResponse.Failed(string.Join("\n", result.Issues.Select(Localize))));
            }
            return Json(OperationResponse.Succeed(_translator.Translate(UserLanguage, "Budget saved")));
        }
        /// <summary>The joint account card: balance, money in and out this cycle, latest transactions.</summary>
        [HttpGet]
        public IActionResult Account()
        {
            var householdId = _budgetLogic.GetHouseholdId(GetUserID());
            return PartialView("_Account", _sepayLogic.GetAccountView(householdId));
        }
        /// <summary>Pulls new transactions from SePay now. POST with a JSON body, like Save.</summary>
        [HttpPost]
        public async Task<IActionResult> SyncAccount([FromBody] object? _)
        {
            var householdId = _budgetLogic.GetHouseholdId(GetUserID());
            var status = await _sepayLogic.SyncHousehold(householdId, HttpContext.RequestAborted);
            if (!status.Succeeded)
            {
                return Json(OperationResponse.Failed(_translator.Translate(UserLanguage, status.Error)));
            }
            return Json(OperationResponse.Succeed(string.Format(_translator.Translate(UserLanguage, "{0} new transactions"), status.Added)));
        }
        private string UserLanguage => _config.GetUserConfig(User).UserLanguage;
        private string Localize(BudgetIssue issue)
        {
            var args = issue.Args.Select(x => x is long amount ? (object)amount.ToString("N0", CultureInfo.CurrentCulture) : x).ToArray();
            try
            {
                return string.Format(_translator.Translate(UserLanguage, issue.Message), args);
            }
            catch (FormatException)
            {
                return issue.ToString();
            }
        }
    }
}
