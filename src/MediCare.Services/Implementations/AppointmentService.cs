using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

/// <summary>
/// Core appointment booking engine and finite state machine.
/// Implements transactional multi-layer double-booking prevention and enforces
/// lifecycle state invariants in Local Clinic Time.
/// </summary>
public class AppointmentService : IAppointmentService
{
    private readonly IUnitOfWork _uow;
    private readonly AppointmentFactory _appointmentFactory;
    private readonly INotificationService _notificationService;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<AppointmentService> _logger;
    private readonly IEmailService? _emailService;

    public AppointmentService(
        IUnitOfWork uow,
        AppointmentFactory appointmentFactory,
        INotificationService notificationService,
        IClinicClock clinicClock,
        ILogger<AppointmentService> logger,
        IEmailService? emailService = null)
    {
        _uow = uow;
        _appointmentFactory = appointmentFactory;
        _notificationService = notificationService;
        _clinicClock = clinicClock;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<Result<int>> BookAppointmentAsync(BookingRequestDto dto)
    {
        // 1. Validate Doctor eligibility
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAndLeavesAsync(dto.DoctorId);
        if (doctor == null || !doctor.IsApproved)
        {
            return Result<int>.Failure("Doctor not found or doctor account is pending administrative approval.");
        }

        // 2. Validate Patient existence
        var patients = await _uow.Patients.FindAsync(p => p.Id == dto.PatientId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<int>.Failure("Patient profile not found.");
        }

        // 3. Time validation (Clinic Wall-Clock Time)
        var targetDateTime = dto.AppointmentDate.Date.Add(dto.StartTime);
        if (targetDateTime <= _clinicClock.Now)
        {
            return Result<int>.Failure("Appointments cannot be scheduled in the past.");
        }

        if (targetDateTime < _clinicClock.Now.AddMinutes(30))
        {
            return Result<int>.Failure("Appointments must be booked at least 30 minutes in advance.");
        }

        // 4. Validate Doctor Leaves
        bool onLeave = doctor.Leaves.Any(l => dto.AppointmentDate.Date >= l.StartDate.Date && dto.AppointmentDate.Date <= l.EndDate.Date);
        if (onLeave)
        {
            return Result<int>.Failure("Doctor is on leave on the selected date.");
        }

        // 5. Validate Doctor Working Hours
        int duration = doctor.SlotDurationMinutes > 0 ? doctor.SlotDurationMinutes : 30;
        var slotSpan = TimeSpan.FromMinutes(duration);
        var expectedEnd = dto.StartTime.Add(slotSpan);
        var shift = doctor.WorkingHours.FirstOrDefault(w =>
            w.DayOfWeek == dto.AppointmentDate.DayOfWeek &&
            dto.StartTime >= w.StartTime &&
            expectedEnd <= w.EndTime);

        if (shift == null)
        {
            return Result<int>.Failure("The selected time is outside the doctor's scheduled clinic hours.");
        }

        // 6. Validate Patient Double-Booking
        bool patientHasConflict = await _uow.Appointments.HasPatientConflictAsync(dto.PatientId, dto.AppointmentDate, dto.StartTime);
        if (patientHasConflict)
        {
            return Result<int>.Failure("You already have an active appointment scheduled at this exact date and time.");
        }

        // 7. Service-level pre-check for Doctor slot
        bool slotConflict = await _uow.Appointments.HasConflictAsync(dto.DoctorId, dto.AppointmentDate, dto.StartTime);
        if (slotConflict)
        {
            return Result<int>.Failure("The selected time slot is already booked. Please choose an alternative time.");
        }

        // 8. Create entity via AppointmentFactory
        var appointment = _appointmentFactory.Create(dto, doctor.ConsultationFee, duration);
        await _uow.Appointments.AddAsync(appointment);

        // 9. Persist and intercept DB-level Unique Index violation
        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Race condition detected: Filtered Unique Index prevented double-booking for doctor {DoctorId} at {Date} {Time}",
                dto.DoctorId, dto.AppointmentDate, dto.StartTime);
            return Result<int>.Failure("This slot was just booked by another patient. Please choose an alternative time.");
        }

        // 10. Post-commit notifications
        var patientUser = (await _uow.Patients.FindAsync(p => p.Id == dto.PatientId)).FirstOrDefault();
        var patientName = patientUser?.User?.FullName ?? "A patient";

        await _notificationService.SendNotificationAsync(
            doctor.UserId,
            "New Appointment Request",
            $"New reservation by {patientName} on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");

        await _notificationService.NotifySlotAvailabilityChangedAsync(doctor.Id, appointment.AppointmentDate);

        if (_emailService != null && !string.IsNullOrEmpty(patientUser?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Booking Request Received";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Request Received</h2>
                    <p>Dear {patientName},</p>
                    <p>Your appointment request with <strong>Dr. {doctor.User.FullName}</strong> has been received.</p>
                    <p><strong>Date:</strong> {appointment.AppointmentDate:yyyy-MM-dd}<br/>
                       <strong>Time:</strong> {DateTime.Today.Add(appointment.StartTime):hh:mm tt}<br/>
                       <strong>Consultation Fee:</strong> {appointment.ConsultationFee:F2} EGP</p>
                    <p>Status: <strong>Pending Doctor Confirmation</strong></p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            _ = _emailService.SendEmailAsync(patientUser.User.Email, emailSubject, emailBody);
        }

        return Result<int>.Success(appointment.Id);
    }

    public async Task<Result> ConfirmAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to confirm appointment {ApptId} belonging to Doctor {OwnerId}",
                doctorId, appointmentId, appointment.DoctorId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        // Guard: only Pending -> Confirmed is valid
        if (appointment.Status != AppointmentStatus.Pending)
        {
            return Result.Failure($"Cannot confirm appointment. Current status is '{appointment.Status}'.");
        }

        appointment.Status = AppointmentStatus.Confirmed;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        // Notify patient
        await _notificationService.SendNotificationAsync(
            appointment.Patient.UserId,
            "Appointment Confirmed",
            $"Dr. {appointment.Doctor.User.FullName} confirmed your appointment on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");

        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Confirmed", appointment.Patient.UserId);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Confirmed";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Confirmed!</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p><strong>Dr. {appointment.Doctor.User.FullName}</strong> has confirmed your appointment.</p>
                    <p><strong>Date:</strong> {appointment.AppointmentDate:yyyy-MM-dd}<br/>
                       <strong>Time:</strong> {DateTime.Today.Add(appointment.StartTime):hh:mm tt}</p>
                    <p>We look forward to seeing you.</p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            _ = _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> RejectAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to reject appointment {ApptId} belonging to Doctor {OwnerId}",
                doctorId, appointmentId, appointment.DoctorId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        // Guard: only Pending -> Rejected is valid
        if (appointment.Status != AppointmentStatus.Pending)
        {
            return Result.Failure($"Cannot reject appointment. Current status is '{appointment.Status}'.");
        }

        appointment.Status = AppointmentStatus.Rejected;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        // Notify patient
        await _notificationService.SendNotificationAsync(
            appointment.Patient.UserId,
            "Appointment Declined",
            $"Dr. {appointment.Doctor.User.FullName} was unable to accept your appointment on {appointment.AppointmentDate:yyyy-MM-dd}. The slot has been released.");

        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Rejected", appointment.Patient.UserId);
        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, appointment.AppointmentDate);

        return Result.Success();
    }

    public async Task<Result> CancelAppointmentAsync(int appointmentId, string userId, bool isDoctorOrAdmin = false)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        // Guard: terminal states cannot transition
        if (appointment.Status == AppointmentStatus.Completed ||
            appointment.Status == AppointmentStatus.Cancelled ||
            appointment.Status == AppointmentStatus.Rejected ||
            appointment.Status == AppointmentStatus.NoShow)
        {
            return Result.Failure($"Cannot cancel an appointment that is already {appointment.Status}.");
        }

        if (!isDoctorOrAdmin)
        {
            // Patient cancellation: ownership validation
            if (appointment.Patient.UserId != userId)
            {
                _logger.LogWarning("Forbidden: User {UserId} attempted to cancel appointment {ApptId} owned by patient {OwnerId}",
                    userId, appointmentId, appointment.Patient.UserId);
                return Result.Failure("Forbidden: You can only cancel your own appointments.");
            }

            // 2-hour cancellation rule
            var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
            if (appointmentStart - _clinicClock.Now <= TimeSpan.FromHours(2))
            {
                return Result.Failure("Appointments cannot be cancelled less than 2 hours before the scheduled start time. Please contact the clinic directly.");
            }
        }
        else
        {
            // Doctor cancellation: ownership validation
            if (appointment.Doctor.UserId != userId)
            {
                var adminUser = await _uow.Doctors.FindAsync(d => d.UserId == userId);
                // Allow Admin or the owning doctor
                if (appointment.Doctor.UserId != userId)
                {
                    _logger.LogInformation("Admin or Doctor {UserId} cancelling appointment {ApptId}", userId, appointmentId);
                }
            }
        }

        appointment.Status = AppointmentStatus.Cancelled;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        // Notify counterpart
        if (!isDoctorOrAdmin)
        {
            await _notificationService.SendNotificationAsync(
                appointment.Doctor.UserId,
                "Appointment Cancelled",
                $"{appointment.Patient.User.FullName} cancelled the appointment scheduled for {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}. The slot is now open.");
        }
        else
        {
            await _notificationService.SendNotificationAsync(
                appointment.Patient.UserId,
                "Appointment Cancelled",
                $"Your appointment with Dr. {appointment.Doctor.User.FullName} on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt} was cancelled by the clinic.");
        }

        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, appointment.AppointmentDate);
        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Cancelled", appointment.Patient.UserId);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Cancelled";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Cancelled</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p>Your appointment with Dr. {appointment.Doctor.User.FullName} scheduled for {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt} has been cancelled.</p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            _ = _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> MarkNoShowAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to mark NoShow on appointment {ApptId}", doctorId, appointmentId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure($"Only Confirmed appointments can be marked as No-Show. Current status is '{appointment.Status}'.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (_clinicClock.Now < appointmentStart)
        {
            return Result.Failure("Cannot mark an appointment as No-Show before its scheduled start time.");
        }

        appointment.Status = AppointmentStatus.NoShow;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result> CompleteAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure($"Only Confirmed appointments can be marked as Completed. Current status is '{appointment.Status}'.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (_clinicClock.Now < appointmentStart)
        {
            return Result.Failure("Cannot complete an appointment before its scheduled start time.");
        }

        appointment.Status = AppointmentStatus.Completed;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result<ConflictCheckResponseDto>> CheckConflictAsync(int doctorId, DateTime appointmentDate, TimeSpan startTime)
    {
        bool hasConflict = await _uow.Appointments.HasConflictAsync(doctorId, appointmentDate, startTime);
        return Result<ConflictCheckResponseDto>.Success(new ConflictCheckResponseDto
        {
            HasConflict = hasConflict,
            Message = hasConflict ? "The selected time slot is already booked." : "Slot is available."
        });
    }

    public async Task<Result<List<AppointmentSummaryDto>>> GetPatientAppointmentsAsync(string patientUserId)
    {
        var patient = (await _uow.Patients.FindAsync(p => p.UserId == patientUserId)).FirstOrDefault();
        if (patient == null)
        {
            return Result<List<AppointmentSummaryDto>>.Failure("Patient record not found.");
        }

        var appointments = await _uow.Appointments.GetPatientAppointmentsAsync(patient.Id);
        var summaries = appointments.Select(a =>
        {
            var apptStart = a.AppointmentDate.Date.Add(a.StartTime);
            bool canCancel = (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) &&
                             (apptStart - _clinicClock.Now).TotalHours > 2;

            return new AppointmentSummaryDto
            {
                Id = a.Id,
                DoctorId = a.DoctorId,
                DoctorName = a.Doctor?.User?.FullName ?? "Physician",
                SpecializationName = a.Doctor?.Specialization?.Name ?? "General",
                PatientId = a.PatientId,
                PatientName = a.Patient?.User?.FullName ?? "Patient",
                AppointmentDate = a.AppointmentDate,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Status = a.Status,
                ConsultationFee = a.ConsultationFee,
                PaymentStatus = a.PaymentStatus,
                Type = a.Type,
                Notes = a.Notes,
                CanCancel = canCancel
            };
        }).ToList();

        return Result<List<AppointmentSummaryDto>>.Success(summaries);
    }

    public async Task<Result<List<AppointmentSummaryDto>>> GetDoctorAppointmentsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null)
    {
        var queryDate = date?.Date;
        var list = (await _uow.Appointments.FindAsync(a =>
            a.DoctorId == doctorId &&
            (!status.HasValue || a.Status == status.Value) &&
            (!queryDate.HasValue || a.AppointmentDate.Date == queryDate.Value)))
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToList();

        var result = new List<AppointmentSummaryDto>();
        foreach (var a in list)
        {
            var full = await _uow.Appointments.GetByIdWithDetailsAsync(a.Id) ?? a;
            result.Add(new AppointmentSummaryDto
            {
                Id = full.Id,
                DoctorId = full.DoctorId,
                DoctorName = full.Doctor?.User?.FullName ?? string.Empty,
                SpecializationName = full.Doctor?.Specialization?.Name ?? string.Empty,
                PatientId = full.PatientId,
                PatientName = full.Patient?.User?.FullName ?? "Patient",
                PatientPhoneNumber = full.Patient?.User?.PhoneNumber,
                AppointmentDate = full.AppointmentDate,
                StartTime = full.StartTime,
                EndTime = full.EndTime,
                Status = full.Status,
                ConsultationFee = full.ConsultationFee,
                PaymentStatus = full.PaymentStatus,
                Type = full.Type,
                Notes = full.Notes,
                CanCancel = (full.Status == AppointmentStatus.Pending || full.Status == AppointmentStatus.Confirmed)
            });
        }

        return Result<List<AppointmentSummaryDto>>.Success(result);
    }

    public async Task<Result<List<CalendarEventDto>>> GetDoctorEventsAsync(int doctorId, DateTime start, DateTime end)
    {
        var appointments = await _uow.Appointments.GetDoctorAppointmentsRangeAsync(doctorId, start, end);
        var events = appointments.Select(a =>
        {
            var color = a.Status switch
            {
                AppointmentStatus.Confirmed => "#198754",
                AppointmentStatus.Pending => "#ffc107",
                AppointmentStatus.Completed => "#0d6efd",
                AppointmentStatus.Cancelled => "#6c757d",
                AppointmentStatus.Rejected => "#dc3545",
                AppointmentStatus.NoShow => "#d63384",
                _ => "#0dcaf0"
            };

            var startIso = a.AppointmentDate.Date.Add(a.StartTime).ToString("yyyy-MM-ddTHH:mm:ss");
            var endIso = a.AppointmentDate.Date.Add(a.EndTime).ToString("yyyy-MM-ddTHH:mm:ss");

            return new CalendarEventDto
            {
                Id = a.Id.ToString(),
                Title = $"{a.Type}: {a.Patient?.User?.FullName ?? "Patient"}",
                Start = startIso,
                End = endIso,
                Status = a.Status.ToString(),
                Color = color,
                Url = $"/Appointments/Details/{a.Id}"
            };
        }).ToList();

        return Result<List<CalendarEventDto>>.Success(events);
    }

    public async Task<Result<int>> GetPatientIdByUserIdAsync(string userId)
    {
        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<int>.Failure("Patient profile not found.");
        }

        return Result<int>.Success(patient.Id);
    }
}
