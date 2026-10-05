using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IAdminService
{
    Task<Result<List<DoctorApprovalSummaryDto>>> GetPendingDoctorsAsync();
    Task<Result> ApproveDoctorAsync(int doctorId);
    Task<Result> RejectDoctorAsync(int doctorId, string? reason);
    Task<Result<AdminDashboardMetricsDto>> GetDashboardMetricsAsync();
    Task<Result<byte[]>> ExportAppointmentsCsvAsync();
}
