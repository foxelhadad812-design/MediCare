using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class AppointmentRepository : Repository<Appointment>, IAppointmentRepository
{
    public AppointmentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateTime date)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId && a.AppointmentDate.Date == targetDate)
            .OrderBy(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsWithDetailsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null)
    {
        var queryDate = date?.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId
                     && (!status.HasValue || a.Status == status.Value)
                     && (!queryDate.HasValue || a.AppointmentDate.Date == queryDate.Value))
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();
    }

    public Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        return HasConflictAsync(doctorId, date, startTime, endTime, null);
    }

    public async Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.DoctorId == doctorId
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDate.Date == targetDate
                        && a.StartTime < endTime
                        && a.EndTime > startTime
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Rejected);
    }

    public async Task<List<Appointment>> GetAppointmentsByMonthAsync(int month, int year)
    {
        return await _context.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentDate.Month == month && a.AppointmentDate.Year == year)
            .ToListAsync();
    }

    public async Task<Appointment?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Include(a => a.MedicalRecord)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Appointment>> GetPatientAppointmentsAsync(int patientId)
    {
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsRangeAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId && a.AppointmentDate.Date >= start && a.AppointmentDate.Date <= end)
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();
    }

    public Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        return HasPatientConflictAsync(patientId, date, startTime, endTime, null);
    }

    public async Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.PatientId == patientId
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDate.Date == targetDate
                        && a.StartTime < endTime
                        && a.EndTime > startTime
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Rejected);
    }

    public async Task<List<Appointment>> GetDoctorActiveAppointmentsInDateRangeAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId
                     && a.AppointmentDate.Date >= start
                     && a.AppointmentDate.Date <= end
                     && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<Dictionary<int, int>> GetAppointmentCountsByPatientAsync(IEnumerable<int> patientIds)
    {
        var idList = patientIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, int>();

        return await _context.Appointments
            .AsNoTracking()
            .Where(a => idList.Contains(a.PatientId))
            .GroupBy(a => a.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Count);
    }

    public async Task<List<Appointment>> GetPendingReminderAppointmentsWithDetailsAsync(DateTime minDate, DateTime maxDate)
    {
        var min = minDate.Date;
        var max = maxDate.Date;
        return await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => !a.ReminderSent
                     && a.AppointmentDate.Date >= min
                     && a.AppointmentDate.Date <= max
                     && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending))
            .ToListAsync();
    }
}
