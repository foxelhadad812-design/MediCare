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

    [Theory]
    [InlineData(20, "10:00", "10:20", "10:20", "10:40", false)] // Adjacent 20m -> No conflict
    [InlineData(20, "10:00", "10:20", "10:10", "10:30", true)]  // Overlap 20m -> Conflict
    [InlineData(30, "10:00", "10:30", "10:30", "11:00", false)] // Adjacent 30m -> No conflict
    [InlineData(30, "10:00", "10:30", "10:15", "10:45", true)]  // Overlap 30m -> Conflict
    [InlineData(45, "10:00", "10:45", "10:45", "11:30", false)] // Adjacent 45m -> No conflict
    [InlineData(45, "10:00", "10:45", "10:30", "11:15", true)]  // Overlap 45m -> Conflict
    [InlineData(60, "10:00", "11:00", "11:00", "12:00", false)] // Adjacent 60m -> No conflict
    [InlineData(60, "10:00", "11:00", "10:30", "11:30", true)]  // Overlap 60m -> Conflict
    public async Task HasConflictAsync_VariousSlotDurations_AccuratelyDetectsOverlapAndAdjacent(
        int duration, string existStart, string existEnd, string reqStart, string reqEnd, bool expectedConflict)
    {
        // Arrange
        var targetDate = new DateTime(2026, 11, 15);
        int docId = 100 + duration + (expectedConflict ? 1 : 0);

        var existingAppt = new Appointment
        {
            Id = docId * 10,
            DoctorId = docId,
            PatientId = 1,
            AppointmentDate = targetDate,
            StartTime = TimeSpan.Parse(existStart),
            EndTime = TimeSpan.Parse(existEnd),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingAppt);
        await _context.SaveChangesAsync();

        // Act
        var hasConflict = await _repository.HasConflictAsync(
            docId, targetDate, TimeSpan.Parse(reqStart), TimeSpan.Parse(reqEnd));

        // Assert
        hasConflict.Should().Be(expectedConflict);
    }

    [Fact]
    public async Task HasPatientConflictAsync_ShouldRejectOverlappingAppointment_AcrossDifferentDoctors()
    {
        // Arrange: Patient 5 has a 45-min appointment with Doctor 1 from 10:00 to 10:45
        var targetDate = new DateTime(2026, 11, 20);
        var existingWithDoc1 = new Appointment
        {
            Id = 801,
            DoctorId = 1,
            PatientId = 5,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 45, 0),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingWithDoc1);
        await _context.SaveChangesAsync();

        // Act 1: Patient attempts to book with Doctor 2 from 10:30 to 11:00 (overlaps with Doctor 1's visit)
        var conflictWithDoc2 = await _repository.HasPatientConflictAsync(
            5, targetDate, new TimeSpan(10, 30, 0), new TimeSpan(11, 0, 0));

        // Act 2: Patient books with Doctor 2 adjacent from 10:45 to 11:15 (back-to-back, no overlap)
        var adjacentWithDoc2 = await _repository.HasPatientConflictAsync(
            5, targetDate, new TimeSpan(10, 45, 0), new TimeSpan(11, 15, 0));

        // Assert
        conflictWithDoc2.Should().BeTrue("Patient cannot have overlapping appointments with two different doctors");
        adjacentWithDoc2.Should().BeFalse("Patient can have back-to-back adjacent appointments");
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
