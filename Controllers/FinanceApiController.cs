using CarCareTracker.External.Interfaces;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using CarCareTracker.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CarCareTracker.Helper;
using System.Globalization;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// JSON API v1 for the native apps. Sign in with an OAuth access token: "Authorization: Bearer ...".
    /// Errors are RFC 9457 problem details; rule violations carry "errors": [{ code, message, args }] so the
    /// apps can show them in their own language. Writes need the Bearer header, so a browser cookie alone
    /// cannot trigger them cross-site. The contract is in wwwroot/api/openapi-v1.json.
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
        private readonly IReconciliationLogic _reconciliationLogic;
        private readonly ISessionLogic _sessionLogic;
        private readonly IReceiptLogic _receiptLogic;
        //a sync asked for within this time returns the last result instead of calling SePay again.
        private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(30);
        private static readonly object budgetLock = new object();
        public FinanceApiController(IBudgetLogic budgetLogic, ISepayLogic sepayLogic, IBankTransactionDataAccess transactionData,
            IReconciliationLogic reconciliationLogic, ISessionLogic sessionLogic, IReceiptLogic receiptLogic)
        {
            _budgetLogic = budgetLogic;
            _sepayLogic = sepayLogic;
            _transactionData = transactionData;
            _reconciliationLogic = reconciliationLogic;
            _sessionLogic = sessionLogic;
            _receiptLogic = receiptLogic;
        }
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        private int HouseholdId => _budgetLogic.GetHouseholdId(UserId);
        private int? SessionId => int.TryParse(User.FindFirstValue(LoginSecurity.SessionClaim), out int id) ? id : null;
        private bool HasBearer => (Request.Headers.Authorization.ToString() ?? string.Empty).StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        private ObjectResult NeedsBearer() => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bearer token required", detail: "Send the OAuth access token in the Authorization header.");
        private ObjectResult RuleProblem(int status, string title, IEnumerable<(string Message, object[] Args)> issues)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: status, title: title);
            problem.Extensions["errors"] = issues.Select(x => new ApiIssueDto
            {
                Code = FinanceApiRules.IssueCode(x.Message),
                Message = x.Args.Length == 0 ? x.Message : string.Format(CultureInfo.InvariantCulture, x.Message, x.Args),
                Args = x.Args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty).ToList()
            }).ToList();
            return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
        }
        private ObjectResult RuleProblem(int status, string title, IEnumerable<string> messages) => RuleProblem(status, title, messages.Select(x => (x, Array.Empty<object>())));

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
        public ActionResult<BudgetDto> Budget()
        {
            var profile = _budgetLogic.GetProfile(UserId);
            if (profile == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No budget yet", detail: "Set up the budget on the website first.");
            }
            Response.Headers.ETag = FinanceApiRules.BudgetETag(profile);
            return FinanceApiRules.ToDto(profile);
        }
        /// <summary>
        /// Replaces the budget plan (the joint account stays as it is). Needs If-Match with the ETag of the
        /// budget the change was made on: when the partner saved in between, the answer is 412 and nothing
        /// is overwritten. Retrying the same request is harmless.
        /// </summary>
        [HttpPut("finance/budget")]
        public ActionResult<BudgetDto> SaveBudget([FromBody] BudgetDto budget)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var ifMatch = Request.Headers.IfMatch.ToString();
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match required", detail: "Send the ETag of the budget you changed in If-Match.");
            }
            lock (budgetLock)
            {
                var current = _budgetLogic.GetProfile(UserId);
                if (current == null)
                {
                    return Problem(statusCode: StatusCodes.Status404NotFound, title: "No budget yet", detail: "Set up the budget on the website first.");
                }
                var currentTag = FinanceApiRules.BudgetETag(current);
                if (!FinanceApiRules.IfMatchAccepts(ifMatch, currentTag))
                {
                    Response.Headers.ETag = currentTag;
                    return Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Changed by someone else", detail: "The budget was saved by someone else in the meantime. Load it again and redo the change.");
                }
                var (profile, error) = FinanceApiRules.FromDto(budget, current, HouseholdId);
                if (profile == null)
                {
                    return RuleProblem(StatusCodes.Status422UnprocessableEntity, "Invalid budget", new[] { error ?? "Invalid budget" });
                }
                var unchanged = FinanceApiRules.BudgetETag(WithStamp(profile, current)) == currentTag;
                if (!unchanged)
                {
                    var result = _budgetLogic.SaveProfile(UserId, profile);
                    if (!result.Success)
                    {
                        return RuleProblem(StatusCodes.Status422UnprocessableEntity, "Invalid budget", result.Issues.Select(x => (x.Message, x.Args)));
                    }
                }
                var saved = _budgetLogic.GetProfile(UserId) ?? profile;
                Response.Headers.ETag = FinanceApiRules.BudgetETag(saved);
                return FinanceApiRules.ToDto(saved);
            }
        }
        //the profile as it would be stored without a new save time, to see whether anything changed.
        private static BudgetProfile WithStamp(BudgetProfile profile, BudgetProfile current)
        {
            profile.UpdatedAtUtc = current.UpdatedAtUtc;
            profile.UpdatedByUserId = current.UpdatedByUserId;
            return profile;
        }

        /// <summary>Changes of the bank balance no transaction explains, the entries made by hand, and the categories.</summary>
        [HttpGet("finance/reconciliation")]
        public ActionResult<ReconciliationDto> Reconciliation()
        {
            return FinanceApiRules.ToDto(_reconciliationLogic.GetView(HouseholdId));
        }
        /// <summary>
        /// Explains (part of) a gap with a category. Send an Idempotency-Key header (a new UUID per entry):
        /// a retry with the same key returns the first entry (200) instead of adding another (201).
        /// </summary>
        [HttpPost("finance/manual-transactions")]
        public ActionResult<TransactionDto> AddManualTransaction([FromBody] ManualTransactionRequest request)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var key = Request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrEmpty(key) && !FinanceApiRules.IsWellFormedIdempotencyKey(key))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid Idempotency-Key", detail: "Use 8 to 64 printable characters, e.g. a UUID.");
            }
            var householdId = HouseholdId;
            var fingerprint = string.IsNullOrEmpty(key) ? null : FinanceApiRules.ManualFingerprint(householdId, UserId, key);
            var result = _reconciliationLogic.AddManualEntry(householdId, UserId, new ManualTransactionInput
            {
                GapKey = request.GapKey ?? string.Empty,
                Amount = request.Amount,
                Date = request.Date ?? string.Empty,
                Category = request.Category ?? string.Empty,
                Note = request.Note ?? string.Empty
            }, fingerprint);
            if (result.Entry == null)
            {
                return RuleProblem(result.GapGone ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity, result.GapGone ? "Gap no longer open" : "Invalid entry", result.Issues);
            }
            var dto = FinanceApiRules.ToDto(result.Entry);
            return result.Replayed ? Ok(dto) : StatusCode(StatusCodes.Status201Created, dto);
        }
        /// <summary>Deletes an entry made by hand. Bank lines cannot be deleted. Deleting twice is harmless (404 the second time).</summary>
        [HttpDelete("finance/manual-transactions/{id:int}")]
        public IActionResult DeleteManualTransaction(int id)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var result = _reconciliationLogic.DeleteManual(HouseholdId, id);
            if (!result.Success)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No such manual entry", detail: result.Message);
            }
            return NoContent();
        }
        /// <summary>Asks SePay for new transactions now. Within 30 seconds of the last sync, its result is returned instead.</summary>
        [HttpPost("finance/account/sync")]
        public async Task<ActionResult<SyncResultDto>> SyncAccount()
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var householdId = HouseholdId;
            var last = _sepayLogic.GetLastSync(householdId);
            if (last?.LastRunUtc is DateTime lastRun && last.Succeeded && DateTime.UtcNow - lastRun < SyncCooldown)
            {
                return ToDto(last, recent: true);
            }
            return ToDto(await _sepayLogic.SyncHousehold(householdId, HttpContext.RequestAborted), recent: false);
        }
        private static SyncResultDto ToDto(AccountSyncStatus status, bool recent) => new()
        {
            Succeeded = status.Succeeded,
            Added = recent ? 0 : status.Added,
            Error = status.Succeeded ? null : FinanceApiRules.IssueCode(string.IsNullOrEmpty(status.Error) ? "Sync failed" : status.Error),
            LastRunUtc = status.LastRunUtc is DateTime run ? DateTime.SpecifyKind(run, DateTimeKind.Utc) : null,
            Recent = recent
        };
        /// <summary>
        /// Reads one photo (JPEG or PNG, at most 8 MB) of a receipt or a bank transfer with the home AI, and
        /// lists the money-out gaps it may explain. Nothing is stored: to record it, explain the chosen gap
        /// with POST finance/manual-transactions.
        /// </summary>
        [HttpPost("finance/receipts/read")]
        [RequestSizeLimit(FoodLogic.MaxImageBytes + 64 * 1024)]
        public async Task<ActionResult<ReceiptReadingDto>> ReadReceipt(IFormFile? file)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var (images, error) = await FoodImages.Load(file == null ? null : new List<IFormFile> { file }, HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(error))
            {
                return RuleProblem(StatusCodes.Status400BadRequest, "Invalid photo", new[] { error == "Take a photo of the label first" ? "Take a photo first" : error });
            }
            var result = await _receiptLogic.Read(HouseholdId, images[0]);
            if (result.Reading == null || !string.IsNullOrEmpty(result.Message))
            {
                var status = _receiptLogic.ModelConfigured && result.Reading != null ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status503ServiceUnavailable;
                return RuleProblem(status, "Could not read the photo", new[] { result.Message });
            }
            return result.Reading;
        }

        /// <summary>Where the user is signed in (apps and browsers), this device first.</summary>
        [HttpGet("me/sessions")]
        public ActionResult<List<SessionDto>> Sessions()
        {
            return _sessionLogic.GetActiveSessions(UserId, SessionId).Select(FinanceApiRules.ToDto).ToList();
        }
        /// <summary>Signs one device out. Its app can no longer refresh its token, and a browser has to sign in again.</summary>
        [HttpDelete("me/sessions/{id:int}")]
        public IActionResult EndSession(int id)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            if (!_sessionLogic.GetActiveSessions(UserId, SessionId).Any(x => x.Id == id) || !_sessionLogic.EndSession(UserId, id))
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No such session");
            }
            return NoContent();
        }
    }
}
