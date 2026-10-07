using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class PrescriptionService : IPrescriptionService
{
    private readonly IUnitOfWork _uow;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<PrescriptionService> _logger;

    public PrescriptionService(
        IUnitOfWork uow,
        IClinicClock clinicClock,
        ILogger<PrescriptionService> logger)
    {
        _uow = uow;
        _clinicClock = clinicClock;
        _logger = logger;
    }

    public async Task<Result<PrescriptionDetailsDto>> GetPrescriptionForPrintAsync(
        int prescriptionId, string userId, bool isDoctor, bool isPatient, bool isAdmin)
    {
        var prescription = await _uow.Prescriptions.GetByIdWithDetailsAsync(prescriptionId);
        if (prescription == null)
        {
            return Result<PrescriptionDetailsDto>.Failure("Prescription not found.");
        }

        // IDOR Authorization Check
        bool isAuthorized = false;
        if (isAdmin)
        {
            isAuthorized = true;
        }
        else if (isDoctor && prescription.Doctor?.UserId == userId)
        {
            isAuthorized = true;
        }
        else if (isPatient && prescription.Patient?.UserId == userId)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to Prescription {PrescriptionId} (PatientOwner: {PatientUserId}, DoctorOwner: {DoctorUserId})",
                userId, prescriptionId, prescription.Patient?.UserId, prescription.Doctor?.UserId);
            return Result<PrescriptionDetailsDto>.Failure("Forbidden: You are not authorized to view this prescription.");
        }

        int? age = null;
        if (prescription.Patient != null && prescription.Patient.DateOfBirth != default)
        {
            var birth = prescription.Patient.DateOfBirth;
            var now = _clinicClock.Today;
            int calculatedAge = now.Year - birth.Year;
            if (birth.Date > now.AddYears(-calculatedAge)) calculatedAge--;
            age = calculatedAge;
        }

        var dto = new PrescriptionDetailsDto
        {
            Id = prescription.Id,
            MedicalRecordId = prescription.MedicalRecordId,
            AppointmentId = prescription.MedicalRecord?.AppointmentId ?? 0,
            PrescriptionDate = prescription.PrescriptionDate,
            DoctorName = prescription.Doctor?.User?.FullName ?? "Unknown Doctor",
            DoctorLicense = prescription.Doctor?.LicenseNumber ?? string.Empty,
            Specialization = prescription.Doctor?.Specialization?.Name ?? string.Empty,
            PatientName = prescription.Patient?.User?.FullName ?? "Unknown Patient",
            PatientAge = age,
            PatientGender = prescription.Patient?.Gender,
            Notes = prescription.Notes,
            Items = prescription.Items.Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                MedicationName = i.MedicationName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions
            }).ToList()
        };

        return Result<PrescriptionDetailsDto>.Success(dto);
    }

    public async Task<Result<PrescriptionDetailsDto>> GetPrescriptionByAppointmentIdAsync(
        int appointmentId, string userId, bool isDoctor, bool isPatient, bool isAdmin)
    {
        var prescription = await _uow.Prescriptions.GetByAppointmentIdWithDetailsAsync(appointmentId);
        if (prescription == null)
        {
            return Result<PrescriptionDetailsDto>.Failure("Prescription not found for this appointment.");
        }

        return await GetPrescriptionForPrintAsync(prescription.Id, userId, isDoctor, isPatient, isAdmin);
    }

    public async Task<Result<PrescriptionDetailsDto>> VerifyPrescriptionAsync(int prescriptionId)
    {
        var prescription = await _uow.Prescriptions.GetByIdWithDetailsAsync(prescriptionId);
        if (prescription == null)
        {
            return Result<PrescriptionDetailsDto>.Failure("Prescription not found or invalid QR code.");
        }

        int? age = null;
        if (prescription.Patient != null && prescription.Patient.DateOfBirth != default)
        {
            var birth = prescription.Patient.DateOfBirth;
            var now = _clinicClock.Today;
            int calculatedAge = now.Year - birth.Year;
            if (birth.Date > now.AddYears(-calculatedAge)) calculatedAge--;
            age = calculatedAge;
        }

        bool isDispensed = prescription.Notes?.Contains("[DISPENSED:") == true;
        string? dispensedNotes = null;
        if (isDispensed && !string.IsNullOrEmpty(prescription.Notes))
        {
            var idx = prescription.Notes.IndexOf("[DISPENSED:");
            dispensedNotes = prescription.Notes.Substring(idx);
        }

        var dto = new PrescriptionDetailsDto
        {
            Id = prescription.Id,
            MedicalRecordId = prescription.MedicalRecordId,
            AppointmentId = prescription.MedicalRecord?.AppointmentId ?? 0,
            PrescriptionDate = prescription.PrescriptionDate,
            DoctorName = prescription.Doctor?.User?.FullName ?? "Physician",
            DoctorLicense = prescription.Doctor?.LicenseNumber ?? string.Empty,
            Specialization = prescription.Doctor?.Specialization?.Name ?? string.Empty,
            PatientName = prescription.Patient?.User?.FullName ?? "Patient",
            PatientAge = age,
            PatientGender = prescription.Patient?.Gender,
            Notes = prescription.Notes,
            IsDispensed = isDispensed,
            DispensedNotes = dispensedNotes,
            Items = prescription.Items.Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                MedicationName = i.MedicationName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions
            }).ToList()
        };

        return Result<PrescriptionDetailsDto>.Success(dto);
    }

    public async Task<Result> MarkPrescriptionDispensedAsync(int prescriptionId, string? pharmacyNotes)
    {
        var prescription = await _uow.Prescriptions.GetByIdWithDetailsAsync(prescriptionId);
        if (prescription == null)
        {
            return Result.Failure("Prescription not found.");
        }

        if (prescription.Notes?.Contains("[DISPENSED:") == true)
        {
            return Result.Failure("Prescription has already been marked as dispensed.");
        }

        var timeStr = _clinicClock.Now.ToString("yyyy-MM-dd HH:mm");
        var noteStamp = $" [DISPENSED: {timeStr} by {pharmacyNotes?.Trim() ?? "Partner Pharmacy"}]";
        prescription.Notes = (prescription.Notes ?? "") + noteStamp;

        _uow.Prescriptions.Update(prescription);
        await _uow.CommitAsync();

        return Result.Success();
    }
}
