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
}
