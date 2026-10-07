using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminService adminService,
        ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    [HttpGet("/Admin")]
    [HttpGet("/Admin/Dashboard")]
    [HttpGet("/Admin/Index")]
    public async Task<IActionResult> Index()
    {
        var metricsResult = await _adminService.GetDashboardMetricsAsync();
        if (!metricsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = metricsResult.Error ?? "Failed to load dashboard metrics.";
            return View(new AdminDashboardMetricsDto());
        }

        return View(metricsResult.Value);
    }

    [HttpGet("/Admin/Approvals")]
    public async Task<IActionResult> Approvals()
    {
        var pendingResult = await _adminService.GetPendingDoctorsAsync();
        if (!pendingResult.IsSuccess)
        {
            TempData["ErrorMessage"] = pendingResult.Error ?? "Failed to load pending doctor applications.";
            return View(new List<DoctorApprovalSummaryDto>());
        }

        return View(pendingResult.Value);
    }

    [HttpPost("/Admin/ApproveDoctor/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDoctor(int id)
    {
        var result = await _adminService.ApproveDoctorAsync(id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Doctor account has been approved and welcoming email dispatched.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to approve doctor.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    [HttpPost("/Admin/RejectDoctor/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDoctor(int id, [FromForm] string? reason)
    {
        var result = await _adminService.RejectDoctorAsync(id, reason);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Doctor application rejected and status email dispatched.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to reject doctor.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    [HttpGet("/Admin/Reports")]
    public async Task<IActionResult> Reports()
    {
        var metricsResult = await _adminService.GetDashboardMetricsAsync();
        if (!metricsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = metricsResult.Error ?? "Failed to load analytics report.";
            return View(new AdminDashboardMetricsDto());
        }

        return View(metricsResult.Value);
    }

    [HttpGet("/Admin/ExportAppointmentsCsv")]
    public async Task<IActionResult> ExportAppointmentsCsv()
    {
        var csvResult = await _adminService.ExportAppointmentsCsvAsync();
        if (!csvResult.IsSuccess || csvResult.Value == null)
        {
            TempData["ErrorMessage"] = csvResult.Error ?? "Failed to generate CSV export.";
            return RedirectToAction(nameof(Reports));
        }

        var fileName = $"medicare-appointments-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        return File(csvResult.Value, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet("/Admin/Patients")]
    public async Task<IActionResult> Patients([FromQuery] string? search)
    {
        ViewData["CurrentSearch"] = search;
        var patientsResult = await _adminService.GetPatientsAsync(search);
        if (!patientsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = patientsResult.Error ?? "Failed to load patient records.";
            return View(new List<AdminPatientSummaryDto>());
        }

        return View(patientsResult.Value);
    }

    [HttpPost("/Admin/TogglePatientLockout/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePatientLockout(int id, [FromForm] bool lockout)
    {
        var result = await _adminService.TogglePatientLockoutAsync(id, lockout);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = lockout
                ? "Patient account has been locked out from accessing the system."
                : "Patient account lockout has been removed.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to update patient lockout status.";
        }

        return RedirectToAction(nameof(Patients));
    }

    [HttpGet("/Admin/Specializations")]
    public async Task<IActionResult> Specializations()
    {
        var specsResult = await _adminService.GetAllSpecializationsAsync();
        if (!specsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = specsResult.Error ?? "Failed to load specializations.";
            return View(new List<SpecializationDto>());
        }

        return View(specsResult.Value);
    }

    [HttpPost("/Admin/CreateSpecialization")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSpecialization([FromForm] CreateSpecializationDto dto)
    {
        var result = await _adminService.CreateSpecializationAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Specialization '{dto.Name}' added successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to add specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost("/Admin/UpdateSpecialization/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSpecialization(int id, [FromForm] UpdateSpecializationDto dto)
    {
        var result = await _adminService.UpdateSpecializationAsync(id, dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Specialization updated successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to update specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost("/Admin/DeleteSpecialization/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSpecialization(int id)
    {
        var result = await _adminService.DeleteSpecializationAsync(id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Specialization deleted successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to delete specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }
}
