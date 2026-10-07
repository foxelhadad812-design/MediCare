using System.Security.Claims;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class PrescriptionsController : Controller
{
    private readonly IPrescriptionService _prescriptionService;
    private readonly ILogger<PrescriptionsController> _logger;

    public PrescriptionsController(
        IPrescriptionService prescriptionService,
        ILogger<PrescriptionsController> logger)
    {
        _prescriptionService = prescriptionService;
        _logger = logger;
    }

    [HttpGet("/Prescriptions/Print/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = User.IsInRole("Admin");

        var result = await _prescriptionService.GetPrescriptionForPrintAsync(id, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from accessing Prescription {PrescriptionId}: {Error}",
                    userId, id, result.Error);
                return Forbid();
            }

            return NotFound();
        }

        return View(result.Value);
    }

    [HttpGet("/Prescriptions/ByAppointment/{appointmentId:int}")]
    public async Task<IActionResult> ByAppointment(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = User.IsInRole("Admin");

        var result = await _prescriptionService.GetPrescriptionByAppointmentIdAsync(appointmentId, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from accessing Prescription for Appointment {ApptId}: {Error}",
                    userId, appointmentId, result.Error);
                return Forbid();
            }

            TempData["ErrorMessage"] = "No prescription record found for this appointment.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        return RedirectToAction(nameof(Print), new { id = result.Value!.Id });
    }

    [HttpGet("/Prescriptions/Verify/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(int id)
    {
        var result = await _prescriptionService.VerifyPrescriptionAsync(id);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = "الروشتة غير موجودة أو كود التحقق غير صالح.";
            return View("VerifyError", result.Error ?? "Prescription not found.");
        }

        return View(result.Value);
    }

    [HttpPost("/Prescriptions/Dispense/{id:int}")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispense(int id, [FromForm] string? pharmacyName)
    {
        var result = await _prescriptionService.MarkPrescriptionDispensedAsync(id, pharmacyName);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "تم تسجيل صرف الروشتة رسمياً بنجاح وتوثيق تاريخ ووقت الصرف.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "فشل تأكيد صرف الروشتة.";
        }

        return RedirectToAction(nameof(Verify), new { id });
    }
}
