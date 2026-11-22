using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// Food label API v1 for the native apps. Same rules as the website: the AI only reads, FoodLabelRules rates.
    /// Writes need an "Authorization: Bearer ..." header, so a browser cookie alone cannot trigger them cross-site.
    /// Messages are in English; the app translates them.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/food")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class FoodApiController : ControllerBase
    {
        private readonly IFoodLogic _foodLogic;
        public FoodApiController(IFoodLogic foodLogic)
        {
            _foodLogic = foodLogic;
        }
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        private bool HasBearer => (Request.Headers.Authorization.ToString() ?? string.Empty).StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        private ObjectResult NeedsBearer() => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Bearer token required", detail: "Send the OAuth access token in the Authorization header.");

        /// <summary>Reads 1 to 3 photos (JPEG or PNG, at most 8 MB each) of one label. The photos are not stored.</summary>
        [HttpPost("read")]
        [RequestSizeLimit(FoodLogic.MaxImages * FoodLogic.MaxImageBytes + 64 * 1024)]
        public async Task<ActionResult<FoodReadResult>> Read([FromForm] List<IFormFile> files)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            var (images, error) = await FoodImages.Load(files, HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(error))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid photos", detail: error);
            }
            return await _foodLogic.ReadLabel(images);
        }
        /// <summary>Rates a checked reading; with save=true it is added to the household history.</summary>
        [HttpPost("analyze")]
        public ActionResult<FoodAnalyzeResult> Analyze([FromBody] FoodAnalyzeRequest request)
        {
            if (!HasBearer)
            {
                return NeedsBearer();
            }
            return _foodLogic.Analyze(UserId, User.Identity?.Name ?? string.Empty, request.Reading ?? new FoodLabelReading(), request.Save);
        }
        /// <summary>Saved scans of the household, newest first.</summary>
        [HttpGet("scans")]
        public ActionResult<List<FoodScan>> Scans([FromQuery] int? limit)
        {
            return _foodLogic.GetHistory(UserId, limit ?? 30);
        }
        [HttpGet("scans/{id:int}")]
        public ActionResult<FoodScan> Scan(int id)
        {
            var scan = _foodLogic.GetScan(UserId, id);
            if (scan == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Scan not found");
            }
            return scan;
        }
    }
}
