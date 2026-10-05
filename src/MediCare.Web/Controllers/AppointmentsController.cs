using System.Security.Claims;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class AppointmentsController : Controller
{
    private readonly IAppointmentService _appointmentService;
    private readonly IDoctorService _doctorService;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(
        IAppointmentService appointmentService,
        IDoctorService doctorService,
        ILogger<AppointmentsController> logger)
    {
        _appointmentService = appointmentService;
        _doctorService = doctorService;
        _logger = logger;
    }

    [HttpGet("/Appointments/Book/{doctorId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Book(int doctorId, [FromQuery] string? date)
    {
        var doctorResult = await _doctorService.GetDoctorDetailsAsync(doctorId);
        if (!doctorResult.IsSuccess || doctorResult.Value == null)
        {
            TempData["ErrorMessage"] = "Doctor not found or account is not approved.";
            return RedirectToAction("Index", "Doctors");
        }

        ViewBag.SelectedDate = date;
        return View(doctorResult.Value);
    }

    [HttpPost("/Appointments/Book")]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookAppointment(
        [FromForm] int doctorId,
        [FromForm] string appointmentDate,
        [FromForm] string startTime,
        [FromForm] string? notes,
        [FromForm] AppointmentType type = AppointmentType.Consultation)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
        if (!patientIdResult.IsSuccess)
        {
            TempData["ErrorMessage"] = "Patient profile record not found. Please contact administration.";
            return RedirectToAction("Index", "Doctors");
        }

        if (!DateTime.TryParse(appointmentDate, out var date) ||
            !TimeSpan.TryParse(startTime, out var time))
        {
            TempData["ErrorMessage"] = "Invalid appointment date or time selected.";
            return RedirectToAction(nameof(Book), new { doctorId });
        }

        var requestDto = new BookingRequestDto
        {
            DoctorId = doctorId,
            PatientId = patientIdResult.Value,
            AppointmentDate = date.Date,
            StartTime = time,
            Notes = notes,
            Type = type
        };

        var result = await _appointmentService.BookAppointmentAsync(requestDto);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to book appointment.";
            return RedirectToAction(nameof(Book), new { doctorId, date = date.ToString("yyyy-MM-dd") });
        }

        TempData["SuccessMessage"] = "Your appointment has been successfully scheduled and is pending doctor confirmation!";
        return RedirectToAction(nameof(MyAppointments));
    }

    [HttpGet("/Appointments/MyAppointments")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> MyAppointments()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _appointmentService.GetPatientAppointmentsAsync(userId);
        var appointments = result.Value ?? new List<AppointmentSummaryDto>();

        return View(appointments);
    }

    [HttpPost("/Appointments/Cancel/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctorOrAdmin = User.IsInRole("Doctor") || User.IsInRole("Admin");
        var result = await _appointmentService.CancelAppointmentAsync(id, userId, isDoctorOrAdmin);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment cancelled successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to cancel appointment.";
        }

        if (User.IsInRole("Doctor"))
        {
            return RedirectToAction("Appointments", "Doctor");
        }

        return RedirectToAction(nameof(MyAppointments));
    }

    [HttpGet("/Appointments/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        // We can inspect appointment through service or direct query
        // Let's get patient appointments or doctor appointments
        if (User.IsInRole("Patient"))
        {
            var appts = await _appointmentService.GetPatientAppointmentsAsync(userId);
            var appt = appts.Value?.FirstOrDefault(a => a.Id == id);
            if (appt != null)
            {
                return View(appt);
            }
        }
        else if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            if (doctorIdResult.IsSuccess)
            {
                var appts = await _appointmentService.GetDoctorAppointmentsAsync(doctorIdResult.Value);
                var appt = appts.Value?.FirstOrDefault(a => a.Id == id);
                if (appt != null)
                {
                    return View(appt);
                }
            }
        }
        else if (User.IsInRole("Admin"))
        {
            // Admin can view any
            var all = await _appointmentService.GetDoctorAppointmentsAsync(0);
            var appt = all.Value?.FirstOrDefault(a => a.Id == id);
            if (appt != null)
            {
                return View(appt);
            }
        }

        _logger.LogWarning("Security: User {UserId} attempted unauthorized IDOR access to appointment {AppointmentId}", userId, id);
        return Forbid();
    }

    [HttpGet("/Appointments/Telehealth/{id:int}")]
    public async Task<IActionResult> Telehealth(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        AppointmentSummaryDto? appt = null;

        if (User.IsInRole("Patient"))
        {
            var appts = await _appointmentService.GetPatientAppointmentsAsync(userId);
            appt = appts.Value?.FirstOrDefault(a => a.Id == id);
        }
        else if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            if (doctorIdResult.IsSuccess)
            {
                var appts = await _appointmentService.GetDoctorAppointmentsAsync(doctorIdResult.Value);
                appt = appts.Value?.FirstOrDefault(a => a.Id == id);
            }
        }
        else if (User.IsInRole("Admin"))
        {
            var all = await _appointmentService.GetDoctorAppointmentsAsync(0);
            appt = all.Value?.FirstOrDefault(a => a.Id == id);
        }

        if (appt == null)
        {
            _logger.LogWarning("Security: User {UserId} denied access to Telehealth session for appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        return View(appt);
    }
}
