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
        private readonly IReconciliationLogic _reconciliationLogic;
        public BudgetController(IBudgetLogic budgetLogic, IConfigHelper config, ITranslationHelper translator, ISepayLogic sepayLogic, IReconciliationLogic reconciliationLogic)
        {
            _reconciliationLogic = reconciliationLogic;
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
        /// <summary>Home dashboard: how the joint account is doing in the current budget cycle.</summary>
        [HttpGet]
        public IActionResult Dashboard()
        {
            var householdId = _budgetLogic.GetHouseholdId(GetUserID());
            return PartialView("_Dashboard", _sepayLogic.GetDashboard(householdId));
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
        /// <summary>Reconciliation of the joint account: unexplained balance changes, manual entries, statement import.</summary>
        [HttpGet]
        public IActionResult Reconciliation()
        {
            var householdId = _budgetLogic.GetHouseholdId(GetUserID());
            return PartialView("_Reconciliation", _reconciliationLogic.GetView(householdId));
        }
        /// <summary>Explains (part of) a balance difference with a category. POST with a JSON body, like Save.</summary>
        [HttpPost]
        public IActionResult AddManualTransaction([FromBody] ManualTransactionInput input)
        {
            if (input == null)
            {
                return Json(OperationResponse.Failed());
            }
            var result = _reconciliationLogic.AddManual(_budgetLogic.GetHouseholdId(GetUserID()), GetUserID(), input);
            return Json(TranslateResponse(result));
        }
        [HttpPost]
        public IActionResult DeleteManualTransaction([FromBody] ManualTransactionDelete input)
        {
            if (input == null)
            {
                return Json(OperationResponse.Failed());
            }
            var result = _reconciliationLogic.DeleteManual(_budgetLogic.GetHouseholdId(GetUserID()), input.Id);
            return Json(TranslateResponse(result));
        }
        /// <summary>
        /// Reads a bank statement (.xlsx or .csv). apply=false only shows what would change; apply=true stores it.
        /// The upload is multipart, so it also needs the X-Requested-With header that only same-site scripts can send.
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(StatementParser.MaxFileBytes + 64 * 1024)]
        public async Task<IActionResult> ImportStatement(IFormFile? file, bool apply = false)
        {
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            {
                return BadRequest();
            }
            if (file == null || file.Length == 0)
            {
                return Json(new StatementImportResult { Errors = new List<string> { _translator.Translate(UserLanguage, "Choose a statement file") } });
            }
            if (file.Length > StatementParser.MaxFileBytes)
            {
                return Json(new StatementImportResult { Errors = new List<string> { _translator.Translate(UserLanguage, "The file is larger than 5 MB") } });
            }
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            var result = _reconciliationLogic.ImportStatement(_budgetLogic.GetHouseholdId(GetUserID()), GetUserID(), buffer.ToArray(), file.FileName, apply);
            result.Errors = result.Errors.Select(x =>
            {
                var text = _translator.Translate(UserLanguage, x);
                return result.AccountMismatch.HasValue && x.Contains("{0}") ? string.Format(text, result.AccountMismatch.Value.Statement, result.AccountMismatch.Value.Shared) : text;
            }).ToList();
            result.Columns = result.Columns.ToDictionary(x => _translator.Translate(UserLanguage, ColumnLabel(x.Key)), x => x.Value);
            result.Message = !result.Success ? string.Empty : result.Applied
                ? string.Format(_translator.Translate(UserLanguage, "Imported: {0} new, {1} replaced manual entries, {2} already recorded"), result.New, result.ReplacesManual, result.AlreadyRecorded)
                : string.Format(_translator.Translate(UserLanguage, "{0} lines read: {1} new, {2} replace manual entries, {3} already recorded"), result.Rows, result.New, result.ReplacesManual, result.AlreadyRecorded);
            return Json(result);
        }
        private static string ColumnLabel(string field) => field switch
        {
            "date" => "Date",
            "description" => "Content",
            "debit" => "Money out",
            "credit" => "Money in",
            "amount" => "Amount",
            "balance" => "Balance",
            "reference" => "Reference",
            _ => field
        };
        private OperationResponse TranslateResponse(OperationResponse response)
        {
            response.Message = string.Join("\n", (response.Message ?? string.Empty).Split('\n').Select(x => _translator.Translate(UserLanguage, x)));
            return response;
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
