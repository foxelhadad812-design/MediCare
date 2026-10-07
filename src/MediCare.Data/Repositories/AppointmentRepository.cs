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
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId && a.AppointmentDate.Date == targetDate)
            .OrderBy(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime)
    {
        return await HasConflictAsync(doctorId, date, startTime, startTime.Add(TimeSpan.FromMinutes(30)));
    }

    public async Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.DoctorId == doctorId
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

    public async Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime)
    {
        return await HasPatientConflictAsync(patientId, date, startTime, startTime.Add(TimeSpan.FromMinutes(30)));
    }

    public async Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.PatientId == patientId
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
}
