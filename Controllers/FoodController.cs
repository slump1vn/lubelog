using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// Food label analysis: photos are read by the home AI, checked by the user, then rated by FoodLabelRules.
    /// Writes take JSON bodies (or multipart with X-Requested-With), so a cross-site form cannot submit them.
    /// </summary>
    [Authorize]
    public class FoodController : Controller
    {
        private readonly IFoodLogic _foodLogic;
        private readonly IConfigHelper _config;
        private readonly ITranslationHelper _translator;
        public FoodController(IFoodLogic foodLogic, IConfigHelper config, ITranslationHelper translator)
        {
            _foodLogic = foodLogic;
            _config = config;
            _translator = translator;
        }
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        private string UserLanguage => _config.GetUserConfig(User).UserLanguage;
        private string T(string text) => _translator.Translate(UserLanguage, text);

        [HttpGet]
        public IActionResult Index()
        {
            return PartialView("_Food", new FoodPageViewModel
            {
                ModelConfigured = _foodLogic.ModelConfigured,
                History = _foodLogic.GetHistory(UserId),
                Profile = _foodLogic.GetProfile(UserId)
            });
        }
        [HttpGet]
        public IActionResult History()
        {
            return PartialView("_FoodHistory", _foodLogic.GetHistory(UserId));
        }
        /// <summary>
        /// Reads up to three photos of one label. The photos stay in memory and are dropped after the answer.
        /// Multipart, so it also needs the X-Requested-With header that only same-site scripts can send.
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(FoodLogic.MaxImages * FoodLogic.MaxImageBytes + 64 * 1024)]
        public async Task<IActionResult> Read(List<IFormFile> files)
        {
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            {
                return BadRequest();
            }
            var (images, error) = await FoodImages.Load(files, HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(error))
            {
                return Json(new FoodReadResult { Message = T(error) });
            }
            var result = await _foodLogic.ReadLabel(images);
            result.Message = T(result.Message);
            return Json(result);
        }
        [HttpPost]
        public IActionResult Analyze([FromBody] FoodAnalyzeRequest request)
        {
            if (request?.Reading == null)
            {
                return BadRequest();
            }
            var result = _foodLogic.Analyze(UserId, User.Identity?.Name ?? string.Empty, request.Reading, request.Save);
            return PartialView("_FoodResult", new FoodResultViewModel
            {
                ScanId = result.ScanId,
                ScannedBy = request.Save ? User.Identity?.Name ?? string.Empty : string.Empty,
                ScannedAtUtc = request.Save ? DateTime.UtcNow : null,
                Message = T(result.Message),
                Reading = result.Reading,
                Analysis = result.Analysis
            });
        }
        [HttpGet]
        public IActionResult Scan(int id)
        {
            var scan = _foodLogic.GetScan(UserId, id);
            if (scan == null)
            {
                return NotFound();
            }
            return PartialView("_FoodResult", new FoodResultViewModel
            {
                ScanId = scan.Id,
                ScannedBy = scan.UserName,
                ScannedAtUtc = scan.CreatedAtUtc,
                TransactionText = scan.TransactionText,
                Reading = scan.Reading,
                Analysis = scan.Analysis
            });
        }
        [HttpPost]
        public IActionResult DeleteScan([FromBody] FoodScanDelete input)
        {
            if (input == null)
            {
                return BadRequest();
            }
            return Json(_foodLogic.DeleteScan(UserId, input.Id) ? OperationResponse.Succeed(T("Scan deleted")) : OperationResponse.Failed(T("Scan not found")));
        }
        [HttpGet]
        public IActionResult Transactions()
        {
            return Json(_foodLogic.GetLinkableTransactions(UserId).Select(x => new { id = x.Id, text = FoodLogic.TransactionText(x) }));
        }
        [HttpPost]
        public IActionResult Link([FromBody] FoodLinkRequest input)
        {
            if (input == null)
            {
                return BadRequest();
            }
            var result = _foodLogic.LinkTransaction(UserId, input.ScanId, input.TransactionId);
            result.Message = T(result.Message);
            return Json(result);
        }
        [HttpPost]
        public IActionResult SaveProfile([FromBody] FoodProfileInput input)
        {
            if (input == null)
            {
                return BadRequest();
            }
            return Json(_foodLogic.SaveProfile(UserId, input.Allergens) ? OperationResponse.Succeed(T("Allergy profile saved")) : OperationResponse.Failed());
        }
        [HttpPost]
        public IActionResult DeleteProfile([FromBody] object? _)
        {
            _foodLogic.DeleteProfile(UserId);
            return Json(OperationResponse.Succeed(T("Allergy profile deleted")));
        }
    }
    /// <summary>Checks uploaded label photos: at most three, each at most 8 MB, JPEG or PNG by content (not by name).</summary>
    public static class FoodImages
    {
        public static bool IsJpegOrPng(byte[] data)
        {
            bool jpeg = data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
            bool png = data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
            return jpeg || png;
        }
        public static async Task<(List<byte[]> Images, string Error)> Load(List<IFormFile>? files, CancellationToken cancellation)
        {
            var images = new List<byte[]>();
            var list = (files ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
            if (!list.Any())
            {
                return (images, "Take a photo of the label first");
            }
            if (list.Count > FoodLogic.MaxImages)
            {
                return (images, "Use at most 3 photos");
            }
            foreach (var file in list)
            {
                if (file.Length > FoodLogic.MaxImageBytes)
                {
                    return (new List<byte[]>(), "Each photo must be smaller than 8 MB");
                }
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellation);
                var data = buffer.ToArray();
                if (!IsJpegOrPng(data))
                {
                    return (new List<byte[]>(), "The image could not be read. Please use a PNG or JPG file");
                }
                images.Add(data);
            }
            return (images, string.Empty);
        }
    }
}
