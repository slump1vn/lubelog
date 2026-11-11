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
        public AiController(IVehicleDataAccess dataAccess, IUserLogic userLogic, IAiLogic aiLogic, IConfigHelper config)
        {
            _dataAccess = dataAccess;
            _userLogic = userLogic;
            _aiLogic = aiLogic;
            _config = config;
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
        [HttpGet]
        public IActionResult Assistant()
        {
            ViewBag.ModelConfigured = _aiLogic.ModelConfigured;
            return PartialView("_Assistant", GetUserVehicles());
        }
        [HttpPost]
        public async Task<IActionResult> Chat(string message)
        {
            var result = await _aiLogic.ProcessMessage(message, GetUserVehicles(), _config.GetUserConfig(User));
            return Json(result);
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
