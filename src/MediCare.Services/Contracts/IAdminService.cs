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

    Task<Result<List<AdminPatientSummaryDto>>> GetPatientsAsync(string? searchTerm = null);
    Task<Result> TogglePatientLockoutAsync(int patientId, bool lockout);

    Task<Result<List<SpecializationDto>>> GetAllSpecializationsAsync();
    Task<Result<SpecializationDto>> GetSpecializationByIdAsync(int id);
    Task<Result<int>> CreateSpecializationAsync(CreateSpecializationDto dto);
    Task<Result> UpdateSpecializationAsync(int id, UpdateSpecializationDto dto);
    Task<Result> DeleteSpecializationAsync(int id);
}
