using System.Security.Claims;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/payment")]
[Authorize]
public class PaymentApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentApiController> _logger;

    public PaymentApiController(
        IPaymentService paymentService,
        ILogger<PaymentApiController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost("checkout")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Checkout([FromBody] PaymentCheckoutRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymentService.ProcessCheckoutAsync(request, userId);
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
}
