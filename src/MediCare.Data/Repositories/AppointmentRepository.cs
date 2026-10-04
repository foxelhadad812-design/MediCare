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
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.DoctorId == doctorId
                        && a.AppointmentDate.Date == targetDate
                        && a.StartTime == startTime
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
}
