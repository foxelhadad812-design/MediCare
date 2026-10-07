using System.Globalization;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class AppointmentReminderService : IAppointmentReminderService
{
    private readonly IUnitOfWork _uow;
    private readonly IClinicClock _clinicClock;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AppointmentReminderService> _logger;
    private readonly UserManager<ApplicationUser>? _userManager;

    public AppointmentReminderService(
        IUnitOfWork uow,
        IClinicClock clinicClock,
        IEmailService emailService,
        ISmsService smsService,
        INotificationService notificationService,
        ILogger<AppointmentReminderService> logger,
        UserManager<ApplicationUser>? userManager = null)
    {
        _uow = uow;
        _clinicClock = clinicClock;
        _emailService = emailService;
        _smsService = smsService;
        _notificationService = notificationService;
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<int> ProcessPendingRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = _clinicClock.Now;
        var windowStart = now.AddHours(23);
        var windowEnd = now.AddHours(25);
        var minDate = windowStart.Date;
        var maxDate = windowEnd.Date;

        // Find candidate active appointments that haven't received a reminder within the target date window
        var candidates = (await _uow.Appointments.FindAsync(a =>
            !a.ReminderSent &&
            a.AppointmentDate >= minDate &&
            a.AppointmentDate <= maxDate &&
            (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending))).ToList();

        var dueAppointments = candidates.Where(a =>
        {
            var apptDateTime = a.AppointmentDate.Date.Add(a.StartTime);
            return apptDateTime >= windowStart && apptDateTime <= windowEnd;
        }).ToList();

        if (dueAppointments.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Processing 24-hour appointment reminders for {Count} appointments", dueAppointments.Count);
        int processedCount = 0;

        foreach (var appt in dueAppointments)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(appt.DoctorId);
                var doctorName = doctor?.User?.FullName ?? $"Doctor #{appt.DoctorId}";
                var specName = doctor?.Specialization?.Name ?? "General Practice";

                var patient = await _uow.Patients.GetByIdAsync(appt.PatientId);
                if (patient == null) continue;

                string? patientEmail = patient.User?.Email;
                string? patientPhone = patient.User?.PhoneNumber;
                string patientName = patient.User?.FullName ?? "Valued Patient";

                if (_userManager != null && string.IsNullOrEmpty(patientEmail))
                {
                    var user = await _userManager.FindByIdAsync(patient.UserId);
                    if (user != null)
                    {
                        patientEmail = user.Email;
                        patientPhone = user.PhoneNumber;
                        if (!string.IsNullOrEmpty(user.FullName)) patientName = user.FullName;
                    }
                }

                var formattedTime = DateTime.Today.Add(appt.StartTime).ToString("hh:mm tt", CultureInfo.InvariantCulture);

                // 1. Dispatch Email Reminder
                if (!string.IsNullOrEmpty(patientEmail))
                {
                    var emailSubject = $"MediCare — Reminder: Appointment Tomorrow with Dr. {doctorName}";
                    var emailBody = $@"
                        <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                            <h2>Hello {patientName},</h2>
                            <p>This is a 24-hour reminder for your upcoming appointment:</p>
                            <ul>
                                <li><strong>Doctor:</strong> Dr. {doctorName}</li>
                                <li><strong>Specialization:</strong> {specName}</li>
                                <li><strong>Date:</strong> {appt.AppointmentDate:dddd, MMMM dd, yyyy}</li>
                                <li><strong>Time:</strong> {formattedTime}</li>
                                <li><strong>Clinic Location:</strong> MediCare Clinic, {doctor?.Governorate ?? "Cairo"}</li>
                            </ul>
                            <p>If you need to reschedule, please access your MediCare patient portal at least 2 hours before the appointment.</p>
                        </div>";

                    await _emailService.SendEmailAsync(patientEmail, emailSubject, emailBody);
                }

                // 2. Dispatch SMS Reminder
                if (!string.IsNullOrEmpty(patientPhone))
                {
                    var smsText = $"MediCare Reminder: You have an appointment tomorrow at {formattedTime} with Dr. {doctorName}.";
                    await _smsService.SendSmsAsync(patientPhone, smsText);
                }

                // 3. Dispatch In-App Notification
                if (!string.IsNullOrEmpty(patient.UserId))
                {
                    await _notificationService.SendNotificationAsync(
                        patient.UserId,
                        "Upcoming Appointment Reminder",
                        $"Reminder: Your appointment with Dr. {doctorName} is tomorrow at {formattedTime}.");
                }

                // 4. Mark reminder as sent
                appt.ReminderSent = true;
                _uow.Appointments.Update(appt);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process reminder for appointment {AppointmentId}", appt.Id);
            }
        }

        if (processedCount > 0)
        {
            await _uow.CommitAsync();
            _logger.LogInformation("Successfully dispatched {ProcessedCount} 24-hour appointment reminders", processedCount);
        }

        return processedCount;
    }
}
