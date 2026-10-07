using System.Globalization;
using System.Text;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

using Microsoft.AspNetCore.Identity;

namespace MediCare.Services.Implementations;

public class AdminService : IAdminService
{
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _emailService;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<AdminService> _logger;
    private readonly UserManager<ApplicationUser>? _userManager;

    public AdminService(
        IUnitOfWork uow,
        IEmailService emailService,
        IClinicClock clinicClock,
        ILogger<AdminService> logger,
        UserManager<ApplicationUser>? userManager = null)
    {
        _uow = uow;
        _emailService = emailService;
        _clinicClock = clinicClock;
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<Result<List<DoctorApprovalSummaryDto>>> GetPendingDoctorsAsync()
    {
        var pendingWithDetails = await _uow.Doctors.GetPendingDoctorsWithDetailsAsync();
        if (pendingWithDetails == null || pendingWithDetails.Count == 0)
        {
            var pending = (await _uow.Doctors.FindAsync(d => !d.IsApproved))?.ToList();
            if (pending != null && pending.Count > 0)
            {
                pendingWithDetails = new List<Doctor>();
                foreach (var doc in pending)
                {
                    var full = await _uow.Doctors.GetDoctorWithDetailsAsync(doc.Id) ?? doc;
                    pendingWithDetails.Add(full);
                }
            }
            else
            {
                pendingWithDetails = new List<Doctor>();
            }
        }

        var resultList = pendingWithDetails.Select(fullDoc => new DoctorApprovalSummaryDto
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
        }).OrderByDescending(d => d.RegisteredAt).ToList();

        return Result<List<DoctorApprovalSummaryDto>>.Success(resultList);
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

            await _emailService.SendEmailAsync(doctor.User.Email, subject, body);
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

        var doctorUser = doctor.User;
        var doctorUserId = doctor.UserId;
        var doctorEmail = doctor.User?.Email;
        var doctorName = doctor.User?.FullName;

        // Delete unapproved doctor record
        _uow.Doctors.Delete(doctor);
        await _uow.CommitAsync();

        // Delete associated orphaned ApplicationUser account so email/credentials are not locked
        if (_userManager != null && (!string.IsNullOrEmpty(doctorUserId) || doctorUser != null))
        {
            var user = doctorUser ?? await _userManager.FindByIdAsync(doctorUserId);
            if (user != null)
            {
                var userDeleteResult = await _userManager.DeleteAsync(user);
                if (!userDeleteResult.Succeeded)
                {
                    _logger.LogWarning("Failed to delete user account {UserId} for rejected doctor: {Errors}",
                        doctorUserId, string.Join(", ", userDeleteResult.Errors.Select(e => e.Description)));
                }
            }
        }

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

            await _emailService.SendEmailAsync(doctorEmail, subject, body);
        }

        return Result.Success();
    }

    public async Task<Result<AdminDashboardMetricsDto>> GetDashboardMetricsAsync()
    {
        var allAppointments = (await _uow.Appointments.GetAllAsync()).ToList();
        var allDoctors = (await _uow.Doctors.GetAllWithDetailsAsync()) ?? (await _uow.Doctors.GetAllAsync()).ToList();

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

        // Top Doctors metric (top 5 by appointment count)
        var topDoctorsList = new List<TopDoctorMetricDto>();
        foreach (var doc in allDoctors)
        {
            var docAppts = allAppointments.Where(a => a.DoctorId == doc.Id).ToList();
            if (docAppts.Count == 0) continue;

            var fullDoc = doc.User != null ? doc : (await _uow.Doctors.GetDoctorWithDetailsAsync(doc.Id) ?? doc);
            var docName = fullDoc?.User?.FullName ?? $"Dr. #{doc.Id}";
            var currentSpec = specializations.FirstOrDefault(s => s.Id == doc.SpecializationId)?.Name ?? "General";
            var revenue = docAppts.Where(a => a.PaymentStatus == PaymentStatus.Paid).Sum(a => a.ConsultationFee);

            topDoctorsList.Add(new TopDoctorMetricDto
            {
                DoctorId = doc.Id,
                DoctorName = docName,
                SpecializationName = currentSpec,
                TotalAppointments = docAppts.Count,
                TotalRevenue = revenue
            });
        }
        metrics.TopDoctors = topDoctorsList.OrderByDescending(d => d.TotalAppointments).ThenByDescending(d => d.TotalRevenue).Take(5).ToList();

        // Demographics metric
        var allPatients = (await _uow.Patients.GetAllAsync()).ToList();
        var today = _clinicClock.Today;
        var demographics = new PatientDemographicsDto
        {
            TotalPatients = allPatients.Count,
            MaleCount = allPatients.Count(p => string.Equals(p.Gender, "Male", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Gender, "M", StringComparison.OrdinalIgnoreCase)),
            FemaleCount = allPatients.Count(p => string.Equals(p.Gender, "Female", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Gender, "F", StringComparison.OrdinalIgnoreCase))
        };

        foreach (var p in allPatients)
        {
            var age = today.Year - p.DateOfBirth.Year;
            if (p.DateOfBirth > today.AddYears(-age)) age--;

            if (age < 18) demographics.AgeUnder18Count++;
            else if (age <= 35) demographics.Age18To35Count++;
            else if (age <= 50) demographics.Age36To50Count++;
            else demographics.AgeOver50Count++;
        }
        metrics.Demographics = demographics;

        return Result<AdminDashboardMetricsDto>.Success(metrics);
    }

    private async Task<List<AppointmentExportRow>> GetAppointmentExportRowsAsync()
    {
        var all = (await _uow.Appointments.GetAllAsync())
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToList();

        var allDoctors = (await _uow.Doctors.GetAllWithDetailsAsync()) ?? (await _uow.Doctors.GetAllAsync()).ToList();
        var docDict = new Dictionary<int, Doctor>();
        foreach (var d in allDoctors)
        {
            var fullDoc = d.User != null ? d : (await _uow.Doctors.GetDoctorWithDetailsAsync(d.Id) ?? d);
            if (fullDoc != null) docDict[d.Id] = fullDoc;
        }

        var allPatients = (await _uow.Patients.GetAllAsync()).ToList();
        var patDict = allPatients.ToDictionary(p => p.Id, p => p);

        var rows = new List<AppointmentExportRow>();
        foreach (var appt in all)
        {
            var docName = docDict.TryGetValue(appt.DoctorId, out var doc) ? (doc.User?.FullName ?? "Unknown") : "Unknown";
            var specName = doc != null && doc.Specialization != null ? doc.Specialization.Name : "General";
            var patName = patDict.TryGetValue(appt.PatientId, out var pat) && pat.User != null ? pat.User.FullName : $"Patient #{appt.PatientId}";

            rows.Add(new AppointmentExportRow
            {
                Id = appt.Id,
                Date = appt.AppointmentDate.ToString("yyyy-MM-dd"),
                Time = appt.StartTime.ToString(@"hh\:mm"),
                DoctorName = docName,
                Specialization = specName,
                PatientName = patName,
                Status = appt.Status.ToString(),
                Fee = appt.ConsultationFee,
                PaymentStatus = appt.PaymentStatus.ToString()
            });
        }

        return rows;
    }

    public async Task<Result<byte[]>> ExportAppointmentsCsvAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var sb = new StringBuilder();

        // Standard CSV Headers
        sb.AppendLine("AppointmentId,Date,Time,Doctor,Specialization,Patient,Status,Fee,PaymentStatus");

        foreach (var r in rows)
        {
            var row = string.Join(",",
                r.Id,
                r.Date,
                r.Time,
                EscapeCsv(r.DoctorName),
                EscapeCsv(r.Specialization),
                EscapeCsv(r.PatientName),
                r.Status,
                r.Fee.ToString("F2", CultureInfo.InvariantCulture),
                r.PaymentStatus
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

    public async Task<Result<byte[]>> ExportAppointmentsExcelAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var bytes = ReportExportGenerators.GenerateExcel(rows);
        return Result<byte[]>.Success(bytes);
    }

    public async Task<Result<byte[]>> ExportAppointmentsPdfAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var bytes = ReportExportGenerators.GeneratePdf(rows);
        return Result<byte[]>.Success(bytes);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Mitigate CSV Formula Injection (CWE-1236)
        if (value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@") || value.StartsWith("\t"))
        {
            value = "'" + value;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    public async Task<Result<List<AdminPatientSummaryDto>>> GetPatientsAsync(string? searchTerm = null)
    {
        var patients = (await _uow.Patients.GetAllAsync()).ToList();
        var allAppointments = (await _uow.Appointments.GetAllAsync()).ToList();
        var apptCounts = allAppointments
            .GroupBy(a => a.PatientId)
            .ToDictionary(g => g.Key, g => g.Count());

        Dictionary<string, ApplicationUser> users = new();
        if (_userManager != null)
        {
            users = _userManager.Users.ToDictionary(u => u.Id, u => u);
        }

        var list = new List<AdminPatientSummaryDto>();
        foreach (var p in patients)
        {
            users.TryGetValue(p.UserId, out var user);
            var fullName = user?.FullName ?? p.User?.FullName ?? $"Patient #{p.Id}";
            var email = user?.Email ?? p.User?.Email ?? string.Empty;
            var phone = user?.PhoneNumber ?? p.User?.PhoneNumber;
            var isLockedOut = user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
            var apptCount = apptCounts.TryGetValue(p.Id, out var count) ? count : 0;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                bool matches = fullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                               email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                               (phone != null && phone.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            list.Add(new AdminPatientSummaryDto
            {
                Id = p.Id,
                UserId = p.UserId,
                FullName = fullName,
                Email = email,
                PhoneNumber = phone,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                BloodGroup = p.BloodGroup,
                EmergencyContact = p.EmergencyContact,
                Allergies = p.Allergies,
                MedicalHistory = p.MedicalHistory,
                IsLockedOut = isLockedOut,
                CreatedAt = p.CreatedAt,
                TotalAppointments = apptCount
            });
        }

        return Result<List<AdminPatientSummaryDto>>.Success(list.OrderBy(p => p.FullName).ToList());
    }

    public async Task<Result> TogglePatientLockoutAsync(int patientId, bool lockout)
    {
        var patient = await _uow.Patients.GetByIdAsync(patientId);
        if (patient == null)
        {
            return Result.Failure("Patient record not found.");
        }

        if (_userManager != null)
        {
            var user = await _userManager.FindByIdAsync(patient.UserId);
            if (user == null)
            {
                return Result.Failure("Patient user account not found.");
            }

            if (lockout)
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            _logger.LogInformation("Admin toggled lockout for patient {PatientId} (User {UserId}): Lockout={Lockout}",
                patientId, patient.UserId, lockout);
        }

        return Result.Success();
    }

    public async Task<Result<List<SpecializationDto>>> GetAllSpecializationsAsync()
    {
        var specs = await _uow.Specializations.GetAllAsync();
        var dtos = specs.Select(s => new SpecializationDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description
        }).OrderBy(s => s.Name).ToList();

        return Result<List<SpecializationDto>>.Success(dtos);
    }

    public async Task<Result<SpecializationDto>> GetSpecializationByIdAsync(int id)
    {
        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result<SpecializationDto>.Failure("Specialization not found.");
        }

        return Result<SpecializationDto>.Success(new SpecializationDto
        {
            Id = spec.Id,
            Name = spec.Name,
            Description = spec.Description
        });
    }

    public async Task<Result<int>> CreateSpecializationAsync(CreateSpecializationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result<int>.Failure("Specialization name is required.");
        }

        var normalizedName = dto.Name.Trim();
        var existing = await _uow.Specializations.FindAsync(s => s.Name.ToLower() == normalizedName.ToLower());
        if (existing.Any())
        {
            return Result<int>.Failure($"A specialization named '{normalizedName}' already exists.");
        }

        var entity = new Specialization
        {
            Name = normalizedName,
            Description = dto.Description?.Trim()
        };

        await _uow.Specializations.AddAsync(entity);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin created specialization {SpecializationName} with Id {SpecializationId}", entity.Name, entity.Id);
        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateSpecializationAsync(int id, UpdateSpecializationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result.Failure("Specialization name is required.");
        }

        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result.Failure("Specialization not found.");
        }

        var normalizedName = dto.Name.Trim();
        var existing = await _uow.Specializations.FindAsync(s => s.Id != id && s.Name.ToLower() == normalizedName.ToLower());
        if (existing.Any())
        {
            return Result.Failure($"Another specialization named '{normalizedName}' already exists.");
        }

        spec.Name = normalizedName;
        spec.Description = dto.Description?.Trim();
        spec.UpdatedAt = _clinicClock.Now;

        _uow.Specializations.Update(spec);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin updated specialization {SpecializationId} to {SpecializationName}", id, spec.Name);
        return Result.Success();
    }

    public async Task<Result> DeleteSpecializationAsync(int id)
    {
        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result.Failure("Specialization not found.");
        }

        var doctors = await _uow.Doctors.FindAsync(d => d.SpecializationId == id);
        if (doctors.Any())
        {
            return Result.Failure($"Cannot delete specialization '{spec.Name}' because {doctors.Count()} doctor(s) are associated with it.");
        }

        _uow.Specializations.Delete(spec);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin deleted specialization {SpecializationId} ({SpecializationName})", id, spec.Name);
        return Result.Success();
    }
}
