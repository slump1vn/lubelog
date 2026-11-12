using CarCareTracker.Logic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    /// <summary>
    /// Receives SePay webhooks for the household's joint account. Open to anonymous callers
    /// because SePay cannot log in; every request must carry a valid HMAC-SHA256 signature
    /// made with LUBELOGGER_SEPAY_WEBHOOK_SECRET, and the endpoint is off when that is not set.
    /// </summary>
    public class SepayController : Controller
    {
        private const int MaxBodyBytes = 64 * 1024;
        private readonly ISepayLogic _sepayLogic;
        public SepayController(ISepayLogic sepayLogic)
        {
            _sepayLogic = sepayLogic;
        }
        [AllowAnonymous]
        [HttpPost]
        [Route("/api/sepay/webhook")]
        [RequestSizeLimit(MaxBodyBytes)]
        public async Task<IActionResult> Webhook()
        {
            //the signature covers the exact bytes SePay sent, so read the body raw.
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, HttpContext.RequestAborted);
            var result = _sepayLogic.ReceiveWebhook(buffer.ToArray(), Request.Headers["X-SePay-Timestamp"], Request.Headers["X-SePay-Signature"], Request.ContentType);
            return result switch
            {
                WebhookResult.Accepted => Ok(new { success = true }),
                WebhookResult.Unauthorized => Unauthorized(new { success = false }),
                WebhookResult.BadRequest => BadRequest(new { success = false }),
                _ => NotFound()
            };
        }
    }
}
