using MediCare.Data.Enums;

namespace MediCare.Services.DTOs;

public class DoctorApprovalSummaryDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string FormattedRegisteredAt => RegisteredAt.ToString("yyyy-MM-dd HH:mm");
}

public class MonthlyStatusMetricDto
{
    public string MonthLabel { get; set; } = string.Empty; // e.g. "2026-05" or "May 2026"
    public int Year { get; set; }
    public int Month { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int NoShowCount { get; set; }
    public decimal PaidRevenue { get; set; }
    public decimal UnpaidRevenue { get; set; }
}

public class SpecializationMetricDto
{
    public string SpecializationName { get; set; } = string.Empty;
    public int AppointmentCount { get; set; }
}

public class AdminDashboardMetricsDto
{
    public int TotalAppointments { get; set; }
    public int ActiveDoctorsCount { get; set; }
    public int PendingDoctorsCount { get; set; }
    public int CompletedVisitsCount { get; set; }
    public decimal TotalRevenueCollected { get; set; }
    public decimal TotalPendingRevenue { get; set; }

    public List<MonthlyStatusMetricDto> MonthlyTrends { get; set; } = new();
    public List<SpecializationMetricDto> SpecializationBreakdown { get; set; } = new();
}
