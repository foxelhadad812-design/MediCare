using MediCare.Data.Entities;

namespace MediCare.Data.Repositories;

public interface IAppointmentRepository : IRepository<Appointment>
{
    Task<List<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateTime date);
    Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime);
    Task<List<Appointment>> GetAppointmentsByMonthAsync(int month, int year);
    Task<Appointment?> GetByIdWithDetailsAsync(int id);
    Task<List<Appointment>> GetPatientAppointmentsAsync(int patientId);
    Task<List<Appointment>> GetDoctorAppointmentsRangeAsync(int doctorId, DateTime startDate, DateTime endDate);
    Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime);
    Task<List<Appointment>> GetDoctorActiveAppointmentsInDateRangeAsync(int doctorId, DateTime startDate, DateTime endDate);
}
