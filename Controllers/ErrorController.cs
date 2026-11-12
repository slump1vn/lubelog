using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CarCareTracker.Controllers
{
    public class ErrorController : Controller
    {
        [Route("Error/{statusCode?}")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Index(int? statusCode)
        {
            var cleanedStatusCode = statusCode ?? 500;
            Response.StatusCode = cleanedStatusCode;
            //the app's API answers errors as RFC 9457 problem details, never as a web page.
            var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
                ?? HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path;
            if (originalPath != null && originalPath.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            {
                return Problem(statusCode: cleanedStatusCode, title: ReasonPhrases.GetReasonPhrase(cleanedStatusCode));
            }
            if (User.IsInRole("APIAuth"))
            {
                return new EmptyResult();
            }
            switch (cleanedStatusCode)
            {
                case 401:
                    return View("401");
                case 403:
                    return View("403");
                case 404:
                    return View("404");
                default:
                    return View("500");
            }
        }
    }
}
