using MediCare.Data.Entities;

namespace MediCare.Data.Repositories;

public interface IDoctorRepository : IRepository<Doctor>
{
    Task<List<Doctor>> GetApprovedDoctorsAsync(int? specializationId);
    Task<Doctor?> GetDoctorWithScheduleAsync(int doctorId);
    Task<(List<Doctor> Doctors, int TotalCount)> SearchApprovedDoctorsAsync(
        int? specializationId,
        decimal? maxFee,
        DayOfWeek? availableDay,
        string? searchTerm,
        int page,
        int pageSize);
    Task<Doctor?> GetDoctorWithDetailsAsync(int id);
}
