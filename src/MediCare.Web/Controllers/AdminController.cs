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
}
