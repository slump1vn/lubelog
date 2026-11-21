using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    [Authorize]
    public class AiController : Controller
    {
        private readonly IVehicleDataAccess _dataAccess;
        private readonly IUserLogic _userLogic;
        private readonly IAiLogic _aiLogic;
        private readonly IConfigHelper _config;
        private readonly IAssistantLogic _assistant;
        private readonly AssistantThrottle _throttle;
        public AiController(IVehicleDataAccess dataAccess, IUserLogic userLogic, IAiLogic aiLogic, IConfigHelper config, IAssistantLogic assistant, AssistantThrottle throttle)
        {
            _dataAccess = dataAccess;
            _userLogic = userLogic;
            _aiLogic = aiLogic;
            _config = config;
            _assistant = assistant;
            _throttle = throttle;
        }
        //writes come from the page's own scripts, which send this header; a cross-site form cannot.
        private bool FromPageScript => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        private IActionResult TooFast()
        {
            Response.Headers.RetryAfter = ((int)AssistantThrottle.Window.TotalSeconds).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new AssistantReply { Kind = "error", Text = "Bạn gửi nhanh quá, đợi một phút rồi thử lại nhé." });
        }
        private int GetUserID()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        }
        private List<Vehicle> GetUserVehicles()
        {
            var vehicles = _dataAccess.GetVehicles();
            if (!User.IsInRole(nameof(UserData.IsRootUser)))
            {
                vehicles = _userLogic.FilterUserVehicles(vehicles, GetUserID());
            }
            return vehicles;
        }
        /// <summary>The chat panel behind the floating button.</summary>
        [HttpGet]
        public IActionResult Assistant()
        {
            var vehicles = GetUserVehicles();
            return PartialView("_Assistant", new AssistantPanelViewModel
            {
                Vehicles = vehicles,
                ModelConfigured = _aiLogic.ModelConfigured,
                Suggestions = _assistant.Suggestions(vehicles.Any())
            });
        }
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult History(int before = 0)
        {
            return Json(_assistant.History(GetUserID(), before, AssistantRules.HistoryPageSize));
        }
        [HttpPost]
        public IActionResult ClearHistory()
        {
            if (!FromPageScript)
            {
                return BadRequest();
            }
            _assistant.ClearHistory(GetUserID());
            return Json(OperationResponse.Succeed());
        }
        [HttpPost]
        public async Task<IActionResult> Chat(string message)
        {
            if (!FromPageScript)
            {
                return BadRequest();
            }
            if (!_throttle.TryAcquire(GetUserID(), DateTime.UtcNow))
            {
                return TooFast();
            }
            return Json(await _assistant.Chat(GetUserID(), message, GetUserVehicles(), _config.GetUserConfig(User)));
        }
        /// <summary>A photo already uploaded to the temp folder; hint "food" reads it as a food label.</summary>
        [HttpPost]
        public async Task<IActionResult> Image(string location, string name, string? hint)
        {
            if (!FromPageScript)
            {
                return BadRequest();
            }
            if (!_throttle.TryAcquire(GetUserID(), DateTime.UtcNow))
            {
                return TooFast();
            }
            return Json(await _assistant.Photo(GetUserID(), location, name, hint, GetUserVehicles(), _config.GetUserConfig(User)));
        }
        [HttpPost]
        public IActionResult AttachPayment([FromBody] AiAttachPaymentRequest request)
        {
            if (request is null || request.RecordId == default)
            {
                return Json(OperationResponse.Failed(StaticHelper.GenericErrorMessage));
            }
            var vehicleId = _aiLogic.GetRecordVehicleId(request.RecordType, request.RecordId);
            if (vehicleId == default || !_userLogic.UserCanEditVehicle(GetUserID(), vehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            return Json(_aiLogic.AttachPayment(request, _config.GetUserConfig(User)));
        }
        [HttpPost]
        public IActionResult Confirm([FromBody] AiDraft draft)
        {
            if (draft is null || draft.VehicleId == default)
            {
                return Json(OperationResponse.Failed("Must provide a valid vehicle id"));
            }
            if (!_userLogic.UserCanEditVehicle(GetUserID(), draft.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            var vehicle = _dataAccess.GetVehicleById(draft.VehicleId);
            if (vehicle is null || vehicle.Id == default)
            {
                return Json(OperationResponse.Failed("Must provide a valid vehicle id"));
            }
            return Json(_aiLogic.SaveDraft(draft, vehicle, _config.GetUserConfig(User), GetUserID(), User.Identity?.Name ?? string.Empty));
        }
    }
}
