using System.Globalization;
using System.Text;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class AdminService : IAdminService
{
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _emailService;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IUnitOfWork uow,
        IEmailService emailService,
        IClinicClock clinicClock,
        ILogger<AdminService> logger)
    {
        _uow = uow;
        _emailService = emailService;
        _clinicClock = clinicClock;
        _logger = logger;
    }

    public async Task<Result<List<DoctorApprovalSummaryDto>>> GetPendingDoctorsAsync()
    {
        var pending = await _uow.Doctors.FindAsync(d => !d.IsApproved);
        // Ensure user and specialization are populated
        var resultList = new List<DoctorApprovalSummaryDto>();

        foreach (var doc in pending)
        {
            var fullDoc = await _uow.Doctors.GetDoctorWithDetailsAsync(doc.Id);
            if (fullDoc != null)
            {
                resultList.Add(new DoctorApprovalSummaryDto
                {
                    Id = fullDoc.Id,
                    UserId = fullDoc.UserId,
                    FullName = fullDoc.User?.FullName ?? "Unknown Doctor",
                    Email = fullDoc.User?.Email ?? string.Empty,
                    PhoneNumber = fullDoc.User?.PhoneNumber ?? string.Empty,
                    SpecializationName = fullDoc.Specialization?.Name ?? string.Empty,
                    LicenseNumber = fullDoc.LicenseNumber,
                    ConsultationFee = fullDoc.ConsultationFee,
                    RegisteredAt = fullDoc.CreatedAt
                });
            }
        }

        return Result<List<DoctorApprovalSummaryDto>>.Success(
            resultList.OrderByDescending(d => d.RegisteredAt).ToList());
    }

    public async Task<Result> ApproveDoctorAsync(int doctorId)
    {
        var doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(doctorId);
        if (doctor == null)
        {
            return Result.Failure("Doctor not found.");
        }

        if (doctor.IsApproved)
        {
            return Result.Failure("Doctor account is already approved.");
        }

        doctor.IsApproved = true;
        _uow.Doctors.Update(doctor);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin approved doctor account: DoctorId={DoctorId}, Name={FullName}",
            doctor.Id, doctor.User?.FullName);

        // Send confirmation email
        if (!string.IsNullOrEmpty(doctor.User?.Email))
        {
            var subject = "MediCare — Doctor Account Approved";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Welcome to MediCare, Dr. {doctor.User.FullName}!</h2>
                    <p>Your physician registration credentials have been verified and approved by the Clinic Administrator.</p>
                    <p>You can now log in to the doctor portal to configure your weekly schedule, manage leaves, and receive patient consultations.</p>
                    <p>Best regards,<br/><strong>MediCare Administration</strong></p>
                </div>";

            _ = _emailService.SendEmailAsync(doctor.User.Email, subject, body);
        }

        return Result.Success();
    }

    public async Task<Result> RejectDoctorAsync(int doctorId, string? reason)
    {
        var doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(doctorId);
        if (doctor == null)
        {
            return Result.Failure("Doctor not found.");
        }

        if (doctor.IsApproved)
        {
            return Result.Failure("Cannot reject an already approved doctor.");
        }

        var doctorEmail = doctor.User?.Email;
        var doctorName = doctor.User?.FullName;

        // Delete unapproved doctor record
        _uow.Doctors.Delete(doctor);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin rejected unapproved doctor registration: DoctorId={DoctorId}, Reason={Reason}",
            doctorId, reason);

        if (!string.IsNullOrEmpty(doctorEmail))
        {
            var subject = "MediCare — Doctor Application Update";
            var rejectionReason = string.IsNullOrWhiteSpace(reason) ? "Application credentials could not be verified at this time." : reason;
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Hello Dr. {doctorName},</h2>
                    <p>Thank you for your interest in joining the MediCare medical network.</p>
                    <p>After review, your application was not approved for the following reason:</p>
                    <blockquote style='background: #f8f9fa; padding: 10px; border-left: 4px solid #dc3545;'>{rejectionReason}</blockquote>
                    <p>Please contact clinic administration for further inquiries.</p>
                </div>";

            _ = _emailService.SendEmailAsync(doctorEmail, subject, body);
        }

        return Result.Success();
    }

    public async Task<Result<AdminDashboardMetricsDto>> GetDashboardMetricsAsync()
    {
        var allAppointments = (await _uow.Appointments.GetAllAsync()).ToList();
        var allDoctors = (await _uow.Doctors.GetAllAsync()).ToList();

        var metrics = new AdminDashboardMetricsDto
        {
            TotalAppointments = allAppointments.Count,
            ActiveDoctorsCount = allDoctors.Count(d => d.IsApproved),
            PendingDoctorsCount = allDoctors.Count(d => !d.IsApproved),
            CompletedVisitsCount = allAppointments.Count(a => a.Status == AppointmentStatus.Completed),
            TotalRevenueCollected = allAppointments
                .Where(a => a.PaymentStatus == PaymentStatus.Paid)
                .Sum(a => a.ConsultationFee),
            TotalPendingRevenue = allAppointments
                .Where(a => a.PaymentStatus == PaymentStatus.Unpaid
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Rejected)
                .Sum(a => a.ConsultationFee)
        };

        // Monthly trends over past 12 months
        var past12Months = Enumerable.Range(0, 12)
            .Select(i => _clinicClock.Today.AddMonths(-i))
            .OrderBy(d => d)
            .Select(d => new { Year = d.Year, Month = d.Month, Label = d.ToString("MMM yyyy", CultureInfo.InvariantCulture) })
            .ToList();

        var trends = new List<MonthlyStatusMetricDto>();
        foreach (var m in past12Months)
        {
            var monthAppts = allAppointments
                .Where(a => a.AppointmentDate.Year == m.Year && a.AppointmentDate.Month == m.Month)
                .ToList();

            trends.Add(new MonthlyStatusMetricDto
            {
                Year = m.Year,
                Month = m.Month,
                MonthLabel = m.Label,
                CompletedCount = monthAppts.Count(a => a.Status == AppointmentStatus.Completed),
                CancelledCount = monthAppts.Count(a => a.Status == AppointmentStatus.Cancelled),
                NoShowCount = monthAppts.Count(a => a.Status == AppointmentStatus.NoShow),
                PaidRevenue = monthAppts.Where(a => a.PaymentStatus == PaymentStatus.Paid).Sum(a => a.ConsultationFee),
                UnpaidRevenue = monthAppts.Where(a => a.PaymentStatus == PaymentStatus.Unpaid).Sum(a => a.ConsultationFee)
            });
        }
        metrics.MonthlyTrends = trends;

        // Specialization breakdown
        var specializations = (await _uow.Specializations.GetAllAsync()).ToList();
        var specDict = specializations.ToDictionary(s => s.Id, s => s.Name);

        var specBreakdown = new List<SpecializationMetricDto>();
        foreach (var spec in specializations)
        {
            // Find appointments for doctors of this specialization
            var specDoctorIds = allDoctors.Where(d => d.SpecializationId == spec.Id).Select(d => d.Id).ToHashSet();
            var count = allAppointments.Count(a => specDoctorIds.Contains(a.DoctorId));

            specBreakdown.Add(new SpecializationMetricDto
            {
                SpecializationName = spec.Name,
                AppointmentCount = count
            });
        }
        metrics.SpecializationBreakdown = specBreakdown.OrderByDescending(s => s.AppointmentCount).ToList();

        return Result<AdminDashboardMetricsDto>.Success(metrics);
    }

    public async Task<Result<byte[]>> ExportAppointmentsCsvAsync()
    {
        var all = (await _uow.Appointments.GetAllAsync())
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToList();

        var sb = new StringBuilder();

        // Standard CSV Headers
        sb.AppendLine("AppointmentId,Date,Time,Doctor,Specialization,Patient,Status,Fee,PaymentStatus");

        var allDoctors = (await _uow.Doctors.GetAllAsync()).ToList();
        var docDict = new Dictionary<int, Doctor>();
        foreach (var d in allDoctors)
        {
            var fullDoc = await _uow.Doctors.GetDoctorWithDetailsAsync(d.Id);
            if (fullDoc != null) docDict[d.Id] = fullDoc;
        }

        var allPatients = (await _uow.Patients.GetAllAsync()).ToList();
        var patDict = allPatients.ToDictionary(p => p.Id, p => p);

        foreach (var appt in all)
        {
            var docName = docDict.TryGetValue(appt.DoctorId, out var doc) ? (doc.User?.FullName ?? "Unknown") : "Unknown";
            var specName = doc != null && doc.Specialization != null ? doc.Specialization.Name : "General";
            var patName = patDict.TryGetValue(appt.PatientId, out var pat) && pat.User != null ? pat.User.FullName : $"Patient #{appt.PatientId}";

            var row = string.Join(",",
                appt.Id,
                appt.AppointmentDate.ToString("yyyy-MM-dd"),
                appt.StartTime.ToString(@"hh\:mm"),
                EscapeCsv(docName),
                EscapeCsv(specName),
                EscapeCsv(patName),
                appt.Status.ToString(),
                appt.ConsultationFee.ToString("F2", CultureInfo.InvariantCulture),
                appt.PaymentStatus.ToString()
            );

            sb.AppendLine(row);
        }

        // UTF-8 with BOM for Excel compatibility
        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + bodyBytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(bodyBytes, 0, result, preamble.Length, bodyBytes.Length);

        return Result<byte[]>.Success(result);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
