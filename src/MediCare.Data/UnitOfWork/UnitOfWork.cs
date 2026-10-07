using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;

namespace MediCare.Data.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IAppointmentRepository? _appointments;
    private IDoctorRepository? _doctors;
    private IRepository<Patient>? _patients;
    private IRepository<Specialization>? _specializations;
    private IRepository<WorkingHours>? _workingHours;
    private IRepository<DoctorLeave>? _doctorLeaves;
    private IMedicalRecordRepository? _medicalRecords;
    private IPrescriptionRepository? _prescriptions;
    private IRepository<PrescriptionItem>? _prescriptionItems;
    private IRepository<Notification>? _notifications;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IAppointmentRepository Appointments =>
        _appointments ??= new AppointmentRepository(_context);

    public IDoctorRepository Doctors =>
        _doctors ??= new DoctorRepository(_context);

    public IRepository<Patient> Patients =>
        _patients ??= new Repository<Patient>(_context);

    public IRepository<Specialization> Specializations =>
        _specializations ??= new Repository<Specialization>(_context);

    public IRepository<WorkingHours> WorkingHours =>
        _workingHours ??= new Repository<WorkingHours>(_context);

    public IRepository<DoctorLeave> DoctorLeaves =>
        _doctorLeaves ??= new Repository<DoctorLeave>(_context);

    public IMedicalRecordRepository MedicalRecords =>
        _medicalRecords ??= new MedicalRecordRepository(_context);

    public IPrescriptionRepository Prescriptions =>
        _prescriptions ??= new PrescriptionRepository(_context);

    public IRepository<PrescriptionItem> PrescriptionItems =>
        _prescriptionItems ??= new Repository<PrescriptionItem>(_context);

    public IRepository<Notification> Notifications =>
        _notifications ??= new Repository<Notification>(_context);

    public async Task<int> CommitAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
