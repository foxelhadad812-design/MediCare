using MediCare.Data.Entities;
using MediCare.Data.Repositories;

namespace MediCare.Data.UnitOfWork;

public interface IUnitOfWork : IDisposable
{
    IAppointmentRepository Appointments { get; }
    IDoctorRepository Doctors { get; }
    IRepository<Patient> Patients { get; }
    IRepository<Specialization> Specializations { get; }
    IRepository<WorkingHours> WorkingHours { get; }
    IRepository<DoctorLeave> DoctorLeaves { get; }
    IRepository<MedicalRecord> MedicalRecords { get; }
    IRepository<Prescription> Prescriptions { get; }
    IRepository<PrescriptionItem> PrescriptionItems { get; }
    IRepository<Notification> Notifications { get; }

    Task<int> CommitAsync();
}
