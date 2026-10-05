using MediCare.Data.Context;
using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class DoctorRepository : Repository<Doctor>, IDoctorRepository
{
    public DoctorRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Doctor>> GetApprovedDoctorsAsync(int? specializationId)
    {
        var query = _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Where(d => d.IsApproved);

        if (specializationId.HasValue)
        {
            query = query.Where(d => d.SpecializationId == specializationId.Value);
        }

        return await query.OrderBy(d => d.User.FullName).ToListAsync();
    }

    public async Task<Doctor?> GetDoctorWithScheduleAsync(int doctorId)
    {
        return await _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.Id == doctorId && d.IsApproved);
    }

    public async Task<(List<Doctor> Doctors, int TotalCount)> SearchApprovedDoctorsAsync(
        int? specializationId,
        decimal? maxFee,
        DayOfWeek? availableDay,
        string? searchTerm,
        int page,
        int pageSize)
    {
        var query = _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Where(d => d.IsApproved);

        if (specializationId.HasValue && specializationId.Value > 0)
        {
            query = query.Where(d => d.SpecializationId == specializationId.Value);
        }

        if (maxFee.HasValue && maxFee.Value > 0)
        {
            query = query.Where(d => d.ConsultationFee <= maxFee.Value);
        }

        if (availableDay.HasValue)
        {
            query = query.Where(d => d.WorkingHours.Any(w => w.DayOfWeek == availableDay.Value));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(d =>
                d.User.FullName.ToLower().Contains(term) ||
                d.Specialization.Name.ToLower().Contains(term) ||
                (d.Bio != null && d.Bio.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var doctors = await query
            .OrderBy(d => d.User.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (doctors, totalCount);
    }

    public async Task<Doctor?> GetDoctorWithDetailsAsync(int id)
    {
        return await _context.Doctors
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .FirstOrDefaultAsync(d => d.Id == id && d.IsApproved);
    }

    public async Task<Doctor?> GetByUserIdAsync(string userId)
    {
        return await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.UserId == userId);
    }

    public async Task<Doctor?> GetDoctorWithScheduleAndLeavesAsync(int doctorId)
    {
        return await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Include(d => d.WorkingHours)
            .Include(d => d.Leaves)
            .FirstOrDefaultAsync(d => d.Id == doctorId);
    }
}
