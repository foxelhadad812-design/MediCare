using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class MedicalRecordService : IMedicalRecordService
{
    private readonly IUnitOfWork _uow;
    private readonly IFileStorageService _fileStorage;
    private readonly IClinicClock _clinicClock;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MedicalRecordService> _logger;

    public MedicalRecordService(
        IUnitOfWork uow,
        IFileStorageService fileStorage,
        IClinicClock clinicClock,
        INotificationService notificationService,
        ILogger<MedicalRecordService> logger)
    {
        _uow = uow;
        _fileStorage = fileStorage;
        _clinicClock = clinicClock;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<AppointmentSummaryDto>> ValidateEncounterAccessAsync(int appointmentId, string doctorUserId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result<AppointmentSummaryDto>.Failure("Appointment not found.");
        }

        var doctor = (await _uow.Doctors.FindAsync(d => d.UserId == doctorUserId)).FirstOrDefault();
        if (doctor == null || appointment.DoctorId != doctor.Id)
        {
            _logger.LogWarning("Security IDOR: Doctor user {DoctorUserId} attempted to open encounter for appointment {ApptId} owned by Doctor {OwnerId}",
                doctorUserId, appointmentId, appointment.DoctorId);
            return Result<AppointmentSummaryDto>.Failure("Forbidden: You are not authorized to document this consultation.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result<AppointmentSummaryDto>.Failure($"Cannot document consultation. Appointment status is currently '{appointment.Status}'. Only Confirmed appointments can be completed.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (appointmentStart > _clinicClock.Now)
        {
            return Result<AppointmentSummaryDto>.Failure("Cannot document consultation before the scheduled appointment start time.");
        }

        var existingRecord = await _uow.MedicalRecords.GetByAppointmentIdWithDetailsAsync(appointmentId);
        if (existingRecord != null)
        {
            return Result<AppointmentSummaryDto>.Failure("A clinical record has already been documented for this appointment.");
        }

        int? age = null;
        if (appointment.Patient != null && appointment.Patient.DateOfBirth != default)
        {
            var birth = appointment.Patient.DateOfBirth;
            var now = _clinicClock.Today;
            age = now.Year - birth.Year - (now.DayOfYear < birth.DayOfYear ? 1 : 0);
        }

        return Result<AppointmentSummaryDto>.Success(new AppointmentSummaryDto
        {
            Id = appointment.Id,
            DoctorId = appointment.DoctorId,
            DoctorName = appointment.Doctor?.User?.FullName ?? "Unknown Doctor",
            SpecializationName = appointment.Doctor?.Specialization?.Name ?? string.Empty,
            PatientId = appointment.PatientId,
            PatientName = appointment.Patient?.User?.FullName ?? "Unknown Patient",
            PatientPhoneNumber = appointment.Patient?.User?.PhoneNumber,
            AppointmentDate = appointment.AppointmentDate,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            Status = appointment.Status,
            ConsultationFee = appointment.ConsultationFee,
            PaymentStatus = appointment.PaymentStatus,
            Type = appointment.Type,
            Notes = appointment.Notes
        });
    }

    public async Task<Result<int>> SaveEncounterAsync(CreateEncounterDto dto, IFormFile? attachment, string doctorUserId, string webRootPath)
    {
        if (string.IsNullOrWhiteSpace(dto.Diagnosis))
        {
            return Result<int>.Failure("Clinical diagnosis is required.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(dto.AppointmentId);
        if (appointment == null)
        {
            return Result<int>.Failure("Appointment not found.");
        }

        var doctor = (await _uow.Doctors.FindAsync(d => d.UserId == doctorUserId)).FirstOrDefault();
        if (doctor == null || appointment.DoctorId != doctor.Id)
        {
            _logger.LogWarning("Security IDOR: Doctor user {DoctorUserId} attempted to submit encounter for appointment {ApptId} owned by Doctor {OwnerId}",
                doctorUserId, dto.AppointmentId, appointment.DoctorId);
            return Result<int>.Failure("Forbidden: You are not authorized to document this consultation.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result<int>.Failure($"Cannot complete encounter. Appointment status is '{appointment.Status}'.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (appointmentStart > _clinicClock.Now)
        {
            return Result<int>.Failure("Cannot complete encounter before the scheduled start time.");
        }

        var existingRecord = await _uow.MedicalRecords.GetByAppointmentIdWithDetailsAsync(dto.AppointmentId);
        if (existingRecord != null)
        {
            return Result<int>.Failure("A medical record has already been completed for this appointment.");
        }

        string? attachmentPath = null;
        if (attachment != null && attachment.Length > 0)
        {
            var uploadResult = await _fileStorage.SaveMedicalAttachmentAsync(attachment, webRootPath);
            if (!uploadResult.IsSuccess)
            {
                return Result<int>.Failure(uploadResult.Error ?? "Failed to upload diagnostic attachment.");
            }
            attachmentPath = uploadResult.Value;
        }

        // 1. Transition Appointment to Completed (Atomic Multi-table Transaction)
        appointment.Status = AppointmentStatus.Completed;
        appointment.PaymentStatus = PaymentStatus.Paid;
        _uow.Appointments.Update(appointment);

        // 2. Insert Medical Record
        var record = new MedicalRecord
        {
            AppointmentId = appointment.Id,
            DoctorId = appointment.DoctorId,
            PatientId = appointment.PatientId,
            Diagnosis = dto.Diagnosis.Trim(),
            Symptoms = dto.Symptoms?.Trim(),
            VisitNotes = dto.VisitNotes?.Trim(),
            AttachmentPath = attachmentPath
        };
        await _uow.MedicalRecords.AddAsync(record);

        // 3. Insert Prescription if items provided
        if (dto.PrescriptionItems != null && dto.PrescriptionItems.Any())
        {
            var prescription = new Prescription
            {
                MedicalRecord = record,
                DoctorId = appointment.DoctorId,
                PatientId = appointment.PatientId,
                PrescriptionDate = _clinicClock.Now,
                Notes = dto.Notes?.Trim(),
                Items = dto.PrescriptionItems.Select(item => new PrescriptionItem
                {
                    MedicationName = item.MedicationName.Trim(),
                    Dosage = item.Dosage.Trim(),
                    Frequency = item.Frequency.Trim(),
                    DurationDays = item.DurationDays,
                    Instructions = item.Instructions?.Trim()
                }).ToList()
            };
            await _uow.Prescriptions.AddAsync(prescription);
        }

        // 4. Save Changes Atomically
        try
        {
            await _uow.CommitAsync();
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(attachmentPath))
            {
                _fileStorage.DeleteAttachment(attachmentPath, webRootPath);
            }
            _logger.LogError(ex, "Failed to commit clinical encounter for appointment {ApptId}", appointment.Id);
            throw;
        }

        // 5. Deliver Post-Commit Notification to Patient
        await _notificationService.SendNotificationAsync(
            appointment.Patient.UserId,
            "Consultation Completed",
            $"Dr. {appointment.Doctor.User.FullName} has finalized your consultation notes and prescription for visit #{appointment.Id}.");

        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Completed", appointment.Patient.UserId);

        _logger.LogInformation("Encounter saved successfully. RecordId={RecordId}, AppointmentId={ApptId}, DoctorId={DoctorId}",
            record.Id, appointment.Id, appointment.DoctorId);

        return Result<int>.Success(record.Id);
    }

    public async Task<Result<MedicalRecordDetailsDto>> GetRecordDetailsAsync(int recordId, string userId, bool isDoctor, bool isPatient, bool isAdmin)
    {
        var record = await _uow.MedicalRecords.GetByIdWithDetailsAsync(recordId);
        if (record == null)
        {
            return Result<MedicalRecordDetailsDto>.Failure("Medical record not found.");
        }

        // IDOR Authorization Enforcement
        bool isAuthorized = false;
        if (isAdmin)
        {
            isAuthorized = true;
        }
        else if (isDoctor && record.Doctor?.UserId == userId)
        {
            isAuthorized = true;
        }
        else if (isPatient && record.Patient?.UserId == userId)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to MedicalRecord {RecordId} (PatientOwner: {PatientUserId}, DoctorOwner: {DocUserId})",
                userId, recordId, record.Patient?.UserId, record.Doctor?.UserId);
            return Result<MedicalRecordDetailsDto>.Failure("Forbidden: You are not authorized to view this clinical record.");
        }

        int? age = null;
        if (record.Patient != null && record.Patient.DateOfBirth != default)
        {
            var birth = record.Patient.DateOfBirth;
            var now = _clinicClock.Today;
            int calculatedAge = now.Year - birth.Year;
            if (birth.Date > now.AddYears(-calculatedAge)) calculatedAge--;
            age = calculatedAge;
        }

        var dto = new MedicalRecordDetailsDto
        {
            Id = record.Id,
            AppointmentId = record.AppointmentId,
            AppointmentDate = record.Appointment.AppointmentDate,
            AppointmentStartTime = record.Appointment.StartTime,
            DoctorId = record.DoctorId,
            DoctorName = record.Doctor?.User?.FullName ?? "Unknown Doctor",
            Specialization = record.Doctor?.Specialization?.Name ?? string.Empty,
            DoctorLicense = record.Doctor?.LicenseNumber ?? string.Empty,
            PatientId = record.PatientId,
            PatientName = record.Patient?.User?.FullName ?? "Unknown Patient",
            PatientGender = record.Patient?.Gender,
            PatientAge = age,
            PatientBloodGroup = record.Patient?.BloodGroup,
            EmergencyContact = record.Patient?.EmergencyContact,
            Diagnosis = record.Diagnosis,
            Symptoms = record.Symptoms,
            VisitNotes = record.VisitNotes,
            AttachmentPath = record.AttachmentPath,
            CreatedAt = record.CreatedAt
        };

        if (record.Prescription != null)
        {
            dto.Prescription = new PrescriptionDetailsDto
            {
                Id = record.Prescription.Id,
                MedicalRecordId = record.Prescription.MedicalRecordId,
                AppointmentId = record.AppointmentId,
                PrescriptionDate = record.Prescription.PrescriptionDate,
                DoctorName = dto.DoctorName,
                DoctorLicense = dto.DoctorLicense,
                Specialization = dto.Specialization,
                PatientName = dto.PatientName,
                PatientAge = dto.PatientAge,
                PatientGender = dto.PatientGender,
                Notes = record.Prescription.Notes,
                Items = record.Prescription.Items.Select(i => new PrescriptionItemDto
                {
                    Id = i.Id,
                    MedicationName = i.MedicationName,
                    Dosage = i.Dosage,
                    Frequency = i.Frequency,
                    DurationDays = i.DurationDays,
                    Instructions = i.Instructions
                }).ToList()
            };
        }

        return Result<MedicalRecordDetailsDto>.Success(dto);
    }

    public async Task<Result<List<MedicalRecordTimelineDto>>> GetPatientTimelineAsync(string patientUserId, string requestingUserId, bool isDoctor, bool isAdmin)
    {
        var targetPatient = (await _uow.Patients.FindAsync(p => p.UserId == patientUserId)).FirstOrDefault();
        if (targetPatient == null)
        {
            return Result<List<MedicalRecordTimelineDto>>.Failure("Patient profile not found.");
        }

        // IDOR Authorization Enforcement
        bool isAuthorized = false;
        if (isAdmin || isDoctor)
        {
            isAuthorized = true;
        }
        else if (patientUserId == requestingUserId)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {RequestingUserId} attempted to view clinical timeline of patient {PatientUserId}",
                requestingUserId, patientUserId);
            return Result<List<MedicalRecordTimelineDto>>.Failure("Forbidden: You cannot view this patient history.");
        }

        var records = await _uow.MedicalRecords.GetPatientHistoryAsync(targetPatient.Id);

        var list = records.Select(r => new MedicalRecordTimelineDto
        {
            Id = r.Id,
            AppointmentId = r.AppointmentId,
            Date = r.Appointment.AppointmentDate,
            Time = r.Appointment.StartTime,
            DoctorName = r.Doctor?.User?.FullName ?? "Dr. Unknown",
            Specialization = r.Doctor?.Specialization?.Name ?? string.Empty,
            Diagnosis = r.Diagnosis,
            Symptoms = r.Symptoms,
            VisitNotes = r.VisitNotes,
            AttachmentPath = r.AttachmentPath,
            PrescriptionId = r.Prescription?.Id
        }).ToList();

        return Result<List<MedicalRecordTimelineDto>>.Success(list);
    }

    public async Task<Result<string>> UploadPatientAttachmentAsync(int appointmentId, IFormFile file, string requestingUserId, string webRootPath)
    {
        if (file == null || file.Length == 0)
        {
            return Result<string>.Failure("No file was provided for upload.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result<string>.Failure("Appointment not found.");
        }

        bool isPatient = appointment.Patient?.UserId == requestingUserId;
        bool isDoctor = appointment.Doctor?.UserId == requestingUserId;
        if (!isPatient && !isDoctor)
        {
            return Result<string>.Failure("Forbidden: You are not authorized to upload diagnostic attachments for this appointment.");
        }

        var uploadResult = await _fileStorage.SaveMedicalAttachmentAsync(file, webRootPath);
        if (!uploadResult.IsSuccess)
        {
            return Result<string>.Failure(uploadResult.Error ?? "Failed to save diagnostic file.");
        }

        var existingRecord = await _uow.MedicalRecords.GetByAppointmentIdWithDetailsAsync(appointmentId);
        if (existingRecord != null)
        {
            existingRecord.AttachmentPath = uploadResult.Value;
            _uow.MedicalRecords.Update(existingRecord);
        }
        else
        {
            var initialRecord = new MedicalRecord
            {
                AppointmentId = appointment.Id,
                DoctorId = appointment.DoctorId,
                PatientId = appointment.PatientId,
                Diagnosis = "Patient Uploaded Diagnostic / Laboratory Files",
                Symptoms = appointment.Notes,
                AttachmentPath = uploadResult.Value
            };
            await _uow.MedicalRecords.AddAsync(initialRecord);
        }

        await _uow.CommitAsync();
        return Result<string>.Success(uploadResult.Value!);
    }
}
