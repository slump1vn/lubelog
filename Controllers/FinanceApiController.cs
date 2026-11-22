using CarCareTracker.External.Interfaces;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// JSON API v1 for the native apps (read-only for now). Sign in with an OAuth access token:
    /// "Authorization: Bearer ...". Errors are RFC 9457 problem details. The contract is in
    /// wwwroot/api/openapi-v1.json.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class FinanceApiController : ControllerBase
    {
        private readonly IBudgetLogic _budgetLogic;
        private readonly ISepayLogic _sepayLogic;
        private readonly IBankTransactionDataAccess _transactionData;
        public FinanceApiController(IBudgetLogic budgetLogic, ISepayLogic sepayLogic, IBankTransactionDataAccess transactionData)
        {
            _budgetLogic = budgetLogic;
            _sepayLogic = sepayLogic;
            _transactionData = transactionData;
        }
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        private int HouseholdId => _budgetLogic.GetHouseholdId(UserId);

        /// <summary>Who is signed in, and which household's money they see.</summary>
        [HttpGet("me")]
        public ActionResult<MeDto> Me()
        {
            return new MeDto
            {
                Id = UserId,
                UserName = User.Identity?.Name ?? string.Empty,
                HouseholdId = HouseholdId,
                IsAdmin = User.IsInRole(nameof(UserData.IsAdmin))
            };
        }
        /// <summary>The home screen: balance, this cycle's money in and out, balance per day, what is coming up.</summary>
        [HttpGet("finance/dashboard")]
        public ActionResult<DashboardDto> Dashboard()
        {
            return FinanceApiRules.ToDto(_sepayLogic.GetDashboard(HouseholdId));
        }
        /// <summary>Transactions of the joint account, newest first, a page at a time.</summary>
        [HttpGet("finance/transactions")]
        public ActionResult<TransactionPageDto> Transactions([FromQuery] int? limit, [FromQuery] string? cursor)
        {
            if (!FinanceApiRules.TryParseCursor(cursor, out var beforeUtc, out int beforeId))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor", detail: "Pass back the nextCursor of the previous page unchanged.");
            }
            var size = FinanceApiRules.ClampPageSize(limit);
            var page = _transactionData.GetPage(HouseholdId, beforeUtc, beforeId, size);
            return new TransactionPageDto
            {
                Items = page.Select(FinanceApiRules.ToDto).ToList(),
                NextCursor = FinanceApiRules.NextCursor(page, size)
            };
        }
        /// <summary>The household budget: paydays, funds, limits and debts. 404 until it is set up on the website.</summary>
        [HttpGet("finance/budget")]
        public ActionResult<BudgetProfile> Budget()
        {
            var profile = _budgetLogic.GetProfile(UserId);
            if (profile == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No budget yet", detail: "Set up the budget on the website first.");
            }
            return profile;
        }
    }
}
