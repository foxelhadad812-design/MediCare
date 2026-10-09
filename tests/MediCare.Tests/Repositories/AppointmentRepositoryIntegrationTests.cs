using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Repositories;

public class AppointmentRepositoryIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AppointmentRepository _repository;

    public AppointmentRepositoryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new AppointmentRepository(_context);
    }

    [Fact]
    public async Task HasConflictAsync_ShouldReturnTrue_WhenRequestedSlotOverlapsExistingAppointmentInterval()
    {
        // Arrange: Existing appointment from 10:00 to 11:00 (e.g. 60-min procedure)
        var targetDate = new DateTime(2026, 11, 15);
        var existingAppt = new Appointment
        {
            Id = 1,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingAppt);
        await _context.SaveChangesAsync();

        // Act: Check conflict for slot at 10:30 (starts at 10:30 inside the [10:00, 11:00) interval)
        var hasConflict = await _repository.HasConflictAsync(1, targetDate, new TimeSpan(10, 30, 0));

        // Assert
        hasConflict.Should().BeTrue();
    }

    [Fact]
    public async Task HasConflictAsync_ShouldReturnFalse_WhenRequestedSlotIsAdjacentBackToBack()
    {
        // Arrange: Existing appointment from 10:00 to 10:30
        var targetDate = new DateTime(2026, 11, 15);
        var existingAppt = new Appointment
        {
            Id = 1,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingAppt);
        await _context.SaveChangesAsync();

        // Act: Slot from 10:30 to 11:00 is immediately after
        var hasConflict = await _repository.HasConflictAsync(1, targetDate, new TimeSpan(10, 30, 0));

        // Assert
        hasConflict.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldIncludeMedicalRecord_WhenRecordExists()
    {
        // Arrange
        var targetDate = new DateTime(2026, 11, 15);
        var doctorUser = new ApplicationUser { Id = "doc_u", FullName = "Dr. Test" };
        var patientUser = new ApplicationUser { Id = "pat_u", FullName = "Patient Test" };
        var spec = new Specialization { Id = 1, Name = "General" };
        var doctor = new Doctor { Id = 10, UserId = "doc_u", SpecializationId = 1, User = doctorUser, Specialization = spec };
        var patient = new Patient { Id = 20, UserId = "pat_u", User = patientUser };

        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 10,
            PatientId = 20,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        var record = new MedicalRecord
        {
            Id = 99,
            AppointmentId = 50,
            DoctorId = 10,
            PatientId = 20,
            Diagnosis = "Hypertension Confirmed",
            AttachmentPath = "uploads/records/scan.pdf"
        };

        _context.Specializations.Add(spec);
        _context.Users.AddRange(doctorUser, patientUser);
        _context.Doctors.Add(doctor);
        _context.Patients.Add(patient);
        _context.Appointments.Add(appt);
        _context.MedicalRecords.Add(record);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var result = await _repository.GetByIdWithDetailsAsync(50);

        // Assert
        result.Should().NotBeNull();
        result!.MedicalRecord.Should().NotBeNull();
        result.MedicalRecord!.Diagnosis.Should().Be("Hypertension Confirmed");
        result.MedicalRecord.AttachmentPath.Should().Be("uploads/records/scan.pdf");
    }

    [Fact]
    public async Task PatientRepository_ShouldAutoIncludeUser_WhenQueryingPatient()
    {
        // Arrange
        var user = new ApplicationUser { Id = "pat_user_auto", FullName = "Sara Ahmed", Email = "sara@patient.com" };
        var patient = new Patient { Id = 35, UserId = "pat_user_auto", User = user };

        _context.Users.Add(user);
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var patientRepo = new Repository<Patient>(_context);

        // Act
        var fetched = (await patientRepo.FindAsync(p => p.Id == 35)).FirstOrDefault();

        // Assert
        fetched.Should().NotBeNull();
        fetched!.User.Should().NotBeNull();
        fetched.User.Email.Should().Be("sara@patient.com");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
