using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPrescriptionService
{
    Task<Result<PrescriptionDetailsDto>> GetPrescriptionForPrintAsync(int prescriptionId, string userId, bool isDoctor, bool isPatient, bool isAdmin);
    Task<Result<PrescriptionDetailsDto>> GetPrescriptionByAppointmentIdAsync(int appointmentId, string userId, bool isDoctor, bool isPatient, bool isAdmin);
    Task<Result<PrescriptionDetailsDto>> VerifyPrescriptionAsync(int prescriptionId);
    Task<Result> MarkPrescriptionDispensedAsync(int prescriptionId, string? pharmacyNotes);
}
