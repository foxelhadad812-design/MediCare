using System.Security.Claims;
using System.Text.Json;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/payment")]
[Authorize]
public class PaymentApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymobCheckoutService _paymob;
    private readonly ILogger<PaymentApiController> _logger;

    public PaymentApiController(
        IPaymentService paymentService,
        IPaymobCheckoutService paymob,
        ILogger<PaymentApiController> logger)
    {
        _paymentService = paymentService;
        _paymob = paymob;
        _logger = logger;
    }

    [HttpPost("checkout")]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout([FromBody] PaymentCheckoutRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymob.CreateCheckoutAsync(request.AppointmentId, userId, request.PromoCode, HttpContext.RequestAborted);
        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            checkoutUrl = result.Value.CheckoutUrl,
            merchantReference = result.Value.MerchantReference,
            status = result.Value.Status
        });
    }

    [HttpPost("paymob/webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> PaymobWebhook([FromBody] JsonElement payload)
    {
        var hmac = Request.Query["hmac"].FirstOrDefault() ?? Request.Headers["hmac"].FirstOrDefault();
        if (!await _paymob.ProcessWebhookAsync(payload, hmac, HttpContext.RequestAborted))
        {
            _logger.LogWarning("Rejected Paymob callback because signature, correlation, or payment details did not validate.");
            return Unauthorized();
        }

        return Ok();
    }

    [HttpGet("paymob/return")]
    [AllowAnonymous]
    public IActionResult PaymobReturn() => Redirect("/Appointments/MyAppointments?payment=pending-verification");

    [HttpGet("receipt/{appointmentId:int}")]
    public async Task<IActionResult> GetReceipt(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymentService.GetReceiptAsync(appointmentId, userId);
        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            receipt = result.Value
        });
    }

    [HttpPost("validate-promo")]
    [Authorize(Roles = "Patient")]
    [EnableRateLimiting("PromoRateLimitPolicy")]
    public async Task<IActionResult> ValidatePromo([FromBody] PromoValidationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { success = false, error = "Promo code is required." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = !string.IsNullOrEmpty(userId)
            ? await _paymentService.ValidatePromoCodeAsync(request.Code, request.Amount, userId)
            : await _paymentService.ValidatePromoCodeAsync(request.Code, request.Amount);

        if (result == null && !string.IsNullOrEmpty(userId))
        {
            result = await _paymentService.ValidatePromoCodeAsync(request.Code, request.Amount);
        }

        if (result == null || !result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result?.Error ?? "Invalid promo code." });
        }

        return Ok(new
        {
            success = true,
            data = result.Value
        });
    }
}

public class PromoValidationRequest
{
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
