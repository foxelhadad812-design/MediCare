using System.Security.Claims;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class AppointmentsController : Controller
{
    private readonly IAppointmentService _appointmentService;
    private readonly IDoctorService _doctorService;
    private readonly IMedicalRecordService _medicalRecordService;
    private readonly INotificationService _notificationService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(
        IAppointmentService appointmentService,
        IDoctorService doctorService,
        IMedicalRecordService medicalRecordService,
        INotificationService notificationService,
        IWebHostEnvironment webHostEnvironment,
        ILogger<AppointmentsController> logger)
    {
        _appointmentService = appointmentService;
        _doctorService = doctorService;
        _medicalRecordService = medicalRecordService;
        _notificationService = notificationService;
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
    }

    [HttpGet("/Appointments/Book/{doctorId:int}")]
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

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        bool isAuthorized = false;

        if (User.IsInRole("Admin"))
        {
            isAuthorized = true;
        }
        else if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            if (doctorIdResult.IsSuccess && appt.DoctorId == doctorIdResult.Value)
            {
                isAuthorized = true;
            }
        }
        else if (User.IsInRole("Patient"))
        {
            var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
            if (patientIdResult.IsSuccess && appt.PatientId == patientIdResult.Value)
            {
                isAuthorized = true;
            }
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        return View(appt);
    }

    [HttpGet("/Appointments/Telehealth/{id:int}")]
    public async Task<IActionResult> Telehealth(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        bool isAuthorized = false;

        if (User.IsInRole("Admin"))
        {
            isAuthorized = true;
        }
        else if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            if (doctorIdResult.IsSuccess && appt.DoctorId == doctorIdResult.Value)
            {
                isAuthorized = true;
            }
        }
        else if (User.IsInRole("Patient"))
        {
            var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
            if (patientIdResult.IsSuccess && appt.PatientId == patientIdResult.Value)
            {
                isAuthorized = true;
            }
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} denied access to Telehealth room for appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        return View(appt);
    }

    [HttpGet("/Appointments/Reschedule/{id:int}")]
    public async Task<IActionResult> Reschedule(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        bool isAuthorized = false;

        if (User.IsInRole("Admin"))
        {
            isAuthorized = true;
        }
        else if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            if (doctorIdResult.IsSuccess && appt.DoctorId == doctorIdResult.Value)
            {
                isAuthorized = true;
            }
        }
        else if (User.IsInRole("Patient"))
        {
            var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
            if (patientIdResult.IsSuccess && appt.PatientId == patientIdResult.Value)
            {
                isAuthorized = true;
            }
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} denied access to reschedule appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        var doctorResult = await _doctorService.GetDoctorDetailsAsync(appt.DoctorId);

        var viewModel = new MediCare.Web.ViewModels.RescheduleAppointmentViewModel
        {
            AppointmentId = appt.Id,
            DoctorId = appt.DoctorId,
            DoctorName = appt.DoctorName,
            SpecializationName = appt.SpecializationName,
            CurrentDate = appt.AppointmentDate,
            CurrentStartTime = appt.StartTime,
            NewAppointmentDate = appt.AppointmentDate > DateTime.Today ? appt.AppointmentDate.AddDays(1) : DateTime.Today.AddDays(1),
            DoctorDetails = doctorResult.Value
        };

        return View(viewModel);
    }

    [HttpPost("/Appointments/Reschedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RescheduleAppointment(
        [FromForm] int appointmentId,
        [FromForm] string newAppointmentDate,
        [FromForm] string newStartTime,
        [FromForm] string? reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (!DateTime.TryParse(newAppointmentDate, out var date) ||
            !TimeSpan.TryParse(newStartTime, out var time))
        {
            TempData["ErrorMessage"] = "Invalid date or time specified for rescheduling.";
            return RedirectToAction(nameof(Reschedule), new { id = appointmentId });
        }

        var dto = new RescheduleRequestDto
        {
            AppointmentId = appointmentId,
            NewAppointmentDate = date.Date,
            NewStartTime = time,
            Reason = reason
        };

        bool isDoctorOrAdmin = User.IsInRole("Doctor") || User.IsInRole("Admin");
        var result = await _appointmentService.RescheduleAppointmentAsync(dto, userId, isDoctorOrAdmin);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment has been successfully rescheduled!";
            if (User.IsInRole("Doctor"))
            {
                return RedirectToAction("Appointments", "Doctor");
            }
            return RedirectToAction(nameof(MyAppointments));
        }

        TempData["ErrorMessage"] = result.Error ?? "Failed to reschedule appointment.";
        return RedirectToAction(nameof(Reschedule), new { id = appointmentId });
    }

    [HttpPost("/Appointments/UploadAttachment/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile? attachment)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (attachment == null || attachment.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid medical file (PDF, PNG, JPG) to upload.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _medicalRecordService.UploadPatientAttachmentAsync(
            id,
            attachment,
            userId,
            _webHostEnvironment.WebRootPath);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Medical lab / radiology attachment uploaded successfully!";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to upload medical attachment.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/Appointments/CallNextQueue/{id:int}")]
    [Authorize(Roles = "Doctor,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallNextQueue(int id)
    {
        var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(doctorUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        var result = await _appointmentService.CallNextQueuePatientAsync(id, doctorUserId, isAdmin);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "تم استدعاء المريض بنجاح وإرسال إشعار فوري للشاشة ولحساب المريض!";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "فشل استدعاء المريض.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
