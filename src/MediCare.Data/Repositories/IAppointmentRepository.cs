using MediCare.Data.Entities;

namespace MediCare.Data.Repositories;

public interface IAppointmentRepository : IRepository<Appointment>
{
    Task<List<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateTime date);
    Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime);
    Task<List<Appointment>> GetAppointmentsByMonthAsync(int month, int year);
}
