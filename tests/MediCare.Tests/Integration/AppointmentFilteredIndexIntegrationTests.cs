using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Integration;

[Trait("Category", "Integration")]
public class AppointmentFilteredIndexIntegrationTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public AppointmentFilteredIndexIntegrationTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("MEDICARE_TEST_CONNECTION_STRING")
            ?? "Server=(localdb)\\mssqllocaldb;Database=MediCare_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";
    }

    private int _doctorId;
    private int _patientId;

    public async Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        // Ensure real SQL Server database schema with Filtered Unique Index is created
        using var context = new ApplicationDbContext(_options);
        await context.Database.EnsureCreatedAsync();

        // Ensure at least one doctor and one patient exist
        if (!await context.Doctors.AnyAsync())
        {
            var spec = await context.Specializations.FirstOrDefaultAsync();
            if (spec == null)
            {
                spec = new Specialization { Name = "Cardiology", Description = "Heart specialist" };
                context.Specializations.Add(spec);
                await context.SaveChangesAsync();
            }

            var docUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "doc_int@test.com", Email = "doc_int@test.com", FullName = "Dr. Test Integrator" };
            var patUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "pat_int@test.com", Email = "pat_int@test.com", FullName = "Test Patient" };
            context.Users.AddRange(docUser, patUser);
            await context.SaveChangesAsync();

            var doc = new Doctor
            {
                UserId = docUser.Id,
                SpecializationId = spec.Id,
                LicenseNumber = "LIC-INT-001",
                ConsultationFee = 350,
                SlotDurationMinutes = 30,
                IsApproved = true
            };

            var pat = new Patient
            {
                UserId = patUser.Id,
                DateOfBirth = new DateTime(1990, 1, 1),
                Gender = "Male"
            };

            context.Doctors.Add(doc);
            context.Patients.Add(pat);
            await context.SaveChangesAsync();
        }

        _doctorId = (await context.Doctors.FirstAsync()).Id;
        _patientId = (await context.Patients.FirstAsync()).Id;
    }

    public async Task DisposeAsync()
    {
        // Cleanup integration test appointments
        try
        {
            using var context = new ApplicationDbContext(_options);
            var testAppts = await context.Appointments
                .Where(a => a.AppointmentDate.Year >= 2027)
                .ToListAsync();
            if (testAppts.Any())
            {
                context.Appointments.RemoveRange(testAppts);
                await context.SaveChangesAsync();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task FilteredIndex_ShouldPreventSecondActiveBooking_OnSameDoctorAndSlot()
    {
        // Arrange: Target doctor 1 at 2027-04-10, 09:00 AM
        var testDate = new DateTime(2027, 4, 10);
        var testSlot = new TimeSpan(9, 0, 0);

        using (var context1 = new ApplicationDbContext(_options))
        {
            var appt1 = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Pending,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation
            };
            context1.Appointments.Add(appt1);
            await context1.SaveChangesAsync();
        }

        // Act & Assert: Second active booking on same slot must trigger DbUpdateException from SQL Server
        using (var context2 = new ApplicationDbContext(_options))
        {
            var appt2 = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Confirmed, // Also active
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation
            };
            context2.Appointments.Add(appt2);

            var act = async () => await context2.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task FilteredIndex_ShouldAllowRebooking_AfterFirstAppointmentIsCancelledOrRejected()
    {
        // Arrange: Target doctor 1 at 2027-05-12, 10:00 AM
        var testDate = new DateTime(2027, 5, 12);
        var testSlot = new TimeSpan(10, 0, 0);

        using (var context = new ApplicationDbContext(_options))
        {
            // 1. Cancelled appointment (Status = 3)
            var apptCancelled = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Cancelled,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation
            };

            // 2. Rejected appointment (Status = 4)
            var apptRejected = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Rejected,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation
            };

            // 3. New active appointment (Status = 0: Pending)
            var apptActive = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Pending,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation
            };

            context.Appointments.AddRange(apptCancelled, apptRejected, apptActive);

            // Act: Save all three records to real SQL Server
            var saveAction = async () => await context.SaveChangesAsync();

            // Assert: All 3 coexist without violating the Filtered Unique Index
            await saveAction.Should().NotThrowAsync();
        }

        // Verify count in DB
        using (var verifyContext = new ApplicationDbContext(_options))
        {
            var count = await verifyContext.Appointments
                .CountAsync(a => a.DoctorId == _doctorId && a.AppointmentDate == testDate && a.StartTime == testSlot);
            count.Should().Be(3);
        }
    }

    [Fact]
    public async Task FilteredIndex_Concurrency_10ParallelBookings_ExactlyOneSucceeds()
    {
        // Arrange: 10 parallel tasks attempting to reserve the exact same slot concurrently
        var testDate = new DateTime(2027, 6, 20);
        var testSlot = new TimeSpan(11, 0, 0);
        int successCount = 0;
        int conflictCount = 0;

        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            // Each task must operate on its own independent DbContext instance
            using var context = new ApplicationDbContext(_options);
            var candidate = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = testDate,
                StartTime = testSlot,
                EndTime = testSlot.Add(TimeSpan.FromMinutes(30)),
                Status = AppointmentStatus.Pending,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Unpaid,
                Type = AppointmentType.Consultation,
                Notes = $"Concurrent Attempt #{i}"
            };

            context.Appointments.Add(candidate);

            try
            {
                await context.SaveChangesAsync();
                Interlocked.Increment(ref successCount);
            }
            catch (DbUpdateException)
            {
                // Expected SQL Server filtered unique index collision
                Interlocked.Increment(ref conflictCount);
            }
        });

        // Act: Execute all 10 simultaneously
        await Task.WhenAll(tasks);

        // Assert: SQL Server ACID serialization guarantees exactly 1 winner and 9 rejected collisions
        successCount.Should().Be(1, "Exactly one appointment booking transaction must commit");
        conflictCount.Should().Be(9, "The remaining 9 concurrent requests must fail with a filtered index conflict");

        // Verify database state has exactly 1 row
        using (var verifyContext = new ApplicationDbContext(_options))
        {
            var bookedAppointments = await verifyContext.Appointments
                .Where(a => a.DoctorId == _doctorId && a.AppointmentDate == testDate && a.StartTime == testSlot)
                .ToListAsync();

            bookedAppointments.Should().HaveCount(1);
        }
    }
}
