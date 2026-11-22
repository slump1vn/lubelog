using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// The chat assistant for the native apps, the same one as the floating button on the website.
    /// Questions only read data; drafts are saved only by the separate confirm calls. Calls that send
    /// something need "Authorization: Bearer ...", so a browser cookie alone cannot trigger them cross-site.
    /// Each user's history is private and deleted after 30 days.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/assistant")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class AssistantApiController : ControllerBase
    {
        private readonly IAssistantLogic _assistant;
        private readonly IAiLogic _aiLogic;
        private readonly IVehicleDataAccess _dataAccess;
        private readonly IUserLogic _userLogic;
        private readonly IConfigHelper _config;
        private readonly AssistantThrottle _throttle;
        private readonly IWebHostEnvironment _webEnv;
        public AssistantApiController(IAssistantLogic assistant, IAiLogic aiLogic, IVehicleDataAccess dataAccess, IUserLogic userLogic, IConfigHelper config, AssistantThrottle throttle, IWebHostEnvironment webEnv)
        {
            _assistant = assistant;
            _aiLogic = aiLogic;
            _dataAccess = dataAccess;
            _userLogic = userLogic;
            _config = config;
            _throttle = throttle;
            _webEnv = webEnv;
        }
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        private bool HasBearer => (Request.Headers.Authorization.ToString() ?? string.Empty).StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        private ObjectResult NeedsBearer() => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bearer token required", detail: "Send the OAuth access token in the Authorization header.");
        private ObjectResult TooFast()
        {
            Response.Headers.RetryAfter = ((int)AssistantThrottle.Window.TotalSeconds).ToString();
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many messages", detail: $"At most {AssistantRules.RequestsPerMinute} messages a minute.");
        }
        private List<Vehicle> UserVehicles()
        {
            var vehicles = _dataAccess.GetVehicles();
            if (!User.IsInRole(nameof(UserData.IsRootUser)))
            {
                vehicles = _userLogic.FilterUserVehicles(vehicles, UserId);
            }
            return vehicles;
        }

        /// <summary>Questions to show as buttons when the chat opens, and whether the home AI is on.</summary>
        [HttpGet("suggestions")]
        public ActionResult<AssistantSuggestionsDto> Suggestions()
        {
            return new AssistantSuggestionsDto { ModelConfigured = _aiLogic.ModelConfigured, Suggestions = _assistant.Suggestions(UserVehicles().Any()) };
        }
        /// <summary>The user's own chat history, oldest first within a page; pass nextBefore back as "before" for older ones.</summary>
        [HttpGet("messages")]
        public ActionResult<AssistantHistoryPage> Messages([FromQuery] int? before, [FromQuery] int? limit)
        {
            return _assistant.History(UserId, before ?? 0, limit ?? AssistantRules.HistoryPageSize);
        }
        /// <summary>Sends one question or log entry and returns the answer (also stored in the history).</summary>
        [HttpPost("messages")]
        public async Task<ActionResult<AssistantReply>> Send([FromBody] AssistantChatRequest request)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var text = (request?.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) || text.Length > AssistantRules.MaxMessageLength)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid text", detail: $"Send 1 to {AssistantRules.MaxMessageLength} characters.");
            }
            if (!_throttle.TryAcquire(UserId, DateTime.UtcNow))
            {
                return TooFast();
            }
            return await _assistant.Chat(UserId, text, UserVehicles(), _config.GetUserConfig(User));
        }
        /// <summary>Deletes the user's whole chat history.</summary>
        [HttpDelete("messages")]
        public IActionResult Clear()
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            _assistant.ClearHistory(UserId);
            return NoContent();
        }
        /// <summary>
        /// Reads one photo (JPEG or PNG, at most 8 MB): a receipt or bank transfer becomes a draft, a food label is
        /// read and rated. hint=food skips the receipt reader. Only a note of the photo is kept in the history.
        /// </summary>
        [HttpPost("photo")]
        [RequestSizeLimit(FoodLogic.MaxImageBytes + 64 * 1024)]
        public async Task<ActionResult<AssistantReply>> Photo([FromForm] IFormFile? file, [FromForm] string? hint)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            if (file == null || file.Length == 0 || file.Length > FoodLogic.MaxImageBytes)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid photo", detail: "Send one JPEG or PNG photo of at most 8 MB.");
            }
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            var bytes = buffer.ToArray();
            if (!FoodImages.IsJpegOrPng(bytes))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid photo", detail: "Send one JPEG or PNG photo of at most 8 MB.");
            }
            if (!_throttle.TryAcquire(UserId, DateTime.UtcNow))
            {
                return TooFast();
            }
            //same temp folder as website uploads, so a receipt draft can attach the photo when it is saved.
            var folder = Path.Combine(_webEnv.ContentRootPath, "data", "temp");
            Directory.CreateDirectory(folder);
            var name = $"{Guid.NewGuid()}{(bytes[0] == 0x89 ? ".png" : ".jpg")}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, HttpContext.RequestAborted);
            return await _assistant.Photo(UserId, $"/temp/{name}", Path.GetFileName(file.FileName ?? name), hint, UserVehicles(), _config.GetUserConfig(User));
        }
        /// <summary>Saves a vehicle draft from an answer, after the user checked it.</summary>
        [HttpPost("drafts")]
        public ActionResult<OperationResponse> SaveDraft([FromBody] AiDraft draft)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            if (draft is null || draft.VehicleId == default)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid draft", detail: "Must provide a valid vehicle id.");
            }
            if (!_userLogic.UserCanEditVehicle(UserId, draft.VehicleId, HouseholdPermission.Edit))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            var vehicle = _dataAccess.GetVehicleById(draft.VehicleId);
            if (vehicle is null || vehicle.Id == default)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid draft", detail: "Must provide a valid vehicle id.");
            }
            return _aiLogic.SaveDraft(draft, vehicle, _config.GetUserConfig(User), UserId, User.Identity?.Name ?? string.Empty);
        }
        /// <summary>Marks an existing vehicle record as paid by a transfer read from a screenshot.</summary>
        [HttpPost("payments")]
        public ActionResult<OperationResponse> AttachPayment([FromBody] AiAttachPaymentRequest request)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            if (request is null || request.RecordId == default)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid payment");
            }
            var vehicleId = _aiLogic.GetRecordVehicleId(request.RecordType, request.RecordId);
            if (vehicleId == default || !_userLogic.UserCanEditVehicle(UserId, vehicleId, HouseholdPermission.Edit))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            return _aiLogic.AttachPayment(request, _config.GetUserConfig(User));
        }
    }
}
namespace CarCareTracker.Models
{
    public class AssistantSuggestionsDto
    {
        public bool ModelConfigured { get; set; }
        public List<string> Suggestions { get; set; } = new List<string>();
    }
}
