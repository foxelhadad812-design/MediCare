using System.Security.Claims;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class PaymentController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IPaymentService paymentService,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpGet("/Appointments/Receipt/{appointmentId:int}")]
    [HttpGet("/Payment/Receipt/{appointmentId:int}")]
    public async Task<IActionResult> Receipt(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _paymentService.GetReceiptAsync(appointmentId, userId);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Unable to load payment receipt.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        return View(result.Value);
    }

    [HttpGet("/Payment/Callback")]
    public async Task<IActionResult> Callback([FromQuery] int? appointmentId, [FromQuery] string? merchant_order_id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        int targetApptId = appointmentId ?? 0;
        if (targetApptId <= 0 && !string.IsNullOrWhiteSpace(merchant_order_id) &&
            merchant_order_id.StartsWith("ORDER-APP", StringComparison.OrdinalIgnoreCase))
        {
            var parts = merchant_order_id.Substring("ORDER-APP".Length).Split('-');
            if (parts.Length > 0) int.TryParse(parts[0], out targetApptId);
        }

        if (targetApptId <= 0)
        {
            TempData["InfoMessage"] = "Payment session completed. Please check your appointment status.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        // Authoritative server-side status check: Never trust query parameters
        var receiptResult = await _paymentService.GetReceiptAsync(targetApptId, userId);
        if (receiptResult.IsSuccess && receiptResult.Value != null)
        {
            TempData["SuccessMessage"] = "Payment was successfully confirmed!";
            return RedirectToAction("Receipt", new { appointmentId = targetApptId });
        }

        TempData["InfoMessage"] = "Your payment transaction is currently processing. Your receipt will appear here once confirmation is finalized.";
        return RedirectToAction("MyAppointments", "Appointments");
    }
}
