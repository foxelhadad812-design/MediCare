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

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
