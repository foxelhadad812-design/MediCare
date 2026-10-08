using System.Security.Claims;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class MedicalRecordsController : Controller
{
    private readonly IMedicalRecordService _medicalRecordService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<MedicalRecordsController> _logger;
    private readonly IFileStorageService _fileStorage;

    public MedicalRecordsController(
        IMedicalRecordService medicalRecordService,
        IWebHostEnvironment webHostEnvironment,
        ILogger<MedicalRecordsController> logger,
        IFileStorageService fileStorage)
    {
        _medicalRecordService = medicalRecordService;
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
        _fileStorage = fileStorage;
    }

    private string GetStorageRootPath()
    {
        var path = Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", "uploads", "records");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    [HttpGet("/MedicalRecords/Create/{appointmentId:int}")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Create(int appointmentId)
    {
        var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(doctorUserId))
        {
            return Challenge();
        }

        var validationResult = await _medicalRecordService.ValidateEncounterAccessAsync(appointmentId, doctorUserId);
        if (!validationResult.IsSuccess)
        {
            if (validationResult.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: Doctor {UserId} forbidden from documenting consultation for appointment {AppointmentId}: {Error}",
                    doctorUserId, appointmentId, validationResult.Error);
                return Forbid();
            }

            TempData["ErrorMessage"] = validationResult.Error;
            return RedirectToAction("Appointments", "Doctor");
        }

        var viewModel = new CreateEncounterViewModel
        {
            Appointment = validationResult.Value!,
            Encounter = new CreateEncounterDto
            {
                AppointmentId = appointmentId,
                PrescriptionItems = new List<PrescriptionItemDto>()
            }
        };

        return View(viewModel);
    }

    [HttpPost("/MedicalRecords/Create")]
    [Authorize(Roles = "Doctor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEncounterViewModel model)
    {
        var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(doctorUserId))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(model.Encounter.Diagnosis))
        {
            ModelState.AddModelError("Encounter.Diagnosis", "Clinical diagnosis is required.");
        }

        // Clean up empty prescription items if any was posted with all blank fields
        if (model.Encounter.PrescriptionItems != null)
        {
            model.Encounter.PrescriptionItems = model.Encounter.PrescriptionItems
                .Where(p => !string.IsNullOrWhiteSpace(p.MedicationName) || !string.IsNullOrWhiteSpace(p.Dosage))
                .ToList();

            for (int i = 0; i < model.Encounter.PrescriptionItems.Count; i++)
            {
                var item = model.Encounter.PrescriptionItems[i];
                if (string.IsNullOrWhiteSpace(item.MedicationName))
                {
                    ModelState.AddModelError($"Encounter.PrescriptionItems[{i}].MedicationName", "Medication name is required.");
                }
                if (string.IsNullOrWhiteSpace(item.Dosage))
                {
                    ModelState.AddModelError($"Encounter.PrescriptionItems[{i}].Dosage", "Dosage is required.");
                }
                if (string.IsNullOrWhiteSpace(item.Frequency))
                {
                    ModelState.AddModelError($"Encounter.PrescriptionItems[{i}].Frequency", "Frequency is required.");
                }
                if (item.DurationDays <= 0)
                {
                    ModelState.AddModelError($"Encounter.PrescriptionItems[{i}].DurationDays", "Duration must be at least 1 day.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            var apptResult = await _medicalRecordService.ValidateEncounterAccessAsync(model.Encounter.AppointmentId, doctorUserId);
            if (apptResult.IsSuccess && apptResult.Value != null)
            {
                model.Appointment = apptResult.Value;
            }
            return View(model);
        }

        var saveResult = await _medicalRecordService.SaveEncounterAsync(
            model.Encounter,
            model.Attachment,
            doctorUserId,
            GetStorageRootPath());

        if (!saveResult.IsSuccess)
        {
            if (saveResult.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: Doctor {UserId} forbidden during encounter creation for appointment {AppointmentId}: {Error}",
                    doctorUserId, model.Encounter.AppointmentId, saveResult.Error);
                return Forbid();
            }

            ModelState.AddModelError("", saveResult.Error ?? "Failed to document consultation.");
            var apptResult = await _medicalRecordService.ValidateEncounterAccessAsync(model.Encounter.AppointmentId, doctorUserId);
            if (apptResult.IsSuccess && apptResult.Value != null)
            {
                model.Appointment = apptResult.Value;
            }
            return View(model);
        }

        TempData["SuccessMessage"] = "Clinical consultation documented and visit marked as Completed.";
        return RedirectToAction(nameof(Details), new { id = saveResult.Value });
    }

    [HttpGet("/MedicalRecords/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = User.IsInRole("Admin");

        var result = await _medicalRecordService.GetRecordDetailsAsync(id, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from accessing MedicalRecord {RecordId}: {Error}",
                    userId, id, result.Error);
                return Forbid();
            }

            return NotFound();
        }

        return View(result.Value);
    }

    [HttpGet("/MedicalRecords/MyHistory")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> MyHistory()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _medicalRecordService.GetPatientTimelineAsync(userId, userId, isDoctor: false, isAdmin: false);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error;
            return View(new List<MedicalRecordTimelineDto>());
        }

        return View(result.Value);
    }

    [HttpGet("/MedicalRecords/DownloadAttachment/{id:int}")]
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = User.IsInRole("Admin");

        var result = await _medicalRecordService.GetRecordDetailsAsync(id, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from downloading attachment of MedicalRecord {RecordId}", userId, id);
                return Forbid();
            }

            return NotFound();
        }

        if (string.IsNullOrEmpty(result.Value?.AttachmentPath))
        {
            return NotFound("No attachment associated with this medical record.");
        }

        var storageRoot = GetStorageRootPath();
        var fullPath = _fileStorage.ResolveAttachmentPath(result.Value.AttachmentPath, storageRoot);

        if (fullPath == null || !System.IO.File.Exists(fullPath))
        {
            return NotFound("Attachment file not found on server.");
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

        var downloadName = $"diagnostic-record-{id}{ext}";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{downloadName}\"";

        return PhysicalFile(fullPath, contentType, downloadName);
    }
}
