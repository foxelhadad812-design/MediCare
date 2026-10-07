using System.Linq.Expressions;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AppointmentReminderServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<IClinicClock> _clockMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ISmsService> _smsServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ILogger<AppointmentReminderService>> _loggerMock;
    private readonly AppointmentReminderService _reminderService;

    public AppointmentReminderServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _clockMock = new Mock<IClinicClock>();
        _emailServiceMock = new Mock<IEmailService>();
        _smsServiceMock = new Mock<ISmsService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _loggerMock = new Mock<ILogger<AppointmentReminderService>>();

        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);

        // Fixed clock: 2026-10-15 10:00 AM
        var currentNow = new DateTime(2026, 10, 15, 10, 0, 0);
        _clockMock.Setup(c => c.Now).Returns(currentNow);

        _reminderService = new AppointmentReminderService(
            _uowMock.Object,
            _clockMock.Object,
            _emailServiceMock.Object,
            _smsServiceMock.Object,
            _notificationServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessPendingRemindersAsync_NoDueAppointments_ReturnsZeroAndDoesNothing()
    {
        // Arrange: appointment is 5 days from now, outside 23-25h window
        var appt = new Appointment
        {
            Id = 1,
            AppointmentDate = new DateTime(2026, 10, 20),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            ReminderSent = false
        };

        _appointmentRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { appt });

        // Act
        var result = await _reminderService.ProcessPendingRemindersAsync();

        // Assert
        result.Should().Be(0);
        _emailServiceMock.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _smsServiceMock.Verify(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessPendingRemindersAsync_AppointmentWithin24hWindow_DispatchesRemindersAndSetsFlag()
    {
        // Arrange: Clock is 2026-10-15 10:00 AM.
        // Appointment is 2026-10-16 10:00 AM (exactly 24 hours away).
        var appt = new Appointment
        {
            Id = 101,
            DoctorId = 5,
            PatientId = 7,
            AppointmentDate = new DateTime(2026, 10, 16),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            ReminderSent = false
        };

        var doctor = new Doctor
        {
            Id = 5,
            Governorate = "Cairo",
            Specialization = new Specialization { Name = "Cardiology" },
            User = new ApplicationUser { FullName = "Dr. Magdi Yacoub" }
        };

        var patient = new Patient
        {
            Id = 7,
            UserId = "patient-user-123",
            User = new ApplicationUser
            {
                FullName = "Ahmed Tarek",
                Email = "ahmed.tarek@example.com",
                PhoneNumber = "+201001234567"
            }
        };

        _appointmentRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { appt });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(5)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(patient);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _reminderService.ProcessPendingRemindersAsync();

        // Assert
        result.Should().Be(1);
        appt.ReminderSent.Should().BeTrue();

        // Verifications
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "ahmed.tarek@example.com",
            It.Is<string>(s => s.Contains("Dr. Magdi Yacoub")),
            It.Is<string>(b => b.Contains("Ahmed Tarek") && b.Contains("Cairo"))), Times.Once);

        _smsServiceMock.Verify(s => s.SendSmsAsync(
            "+201001234567",
            It.Is<string>(m => m.Contains("Dr. Magdi Yacoub"))), Times.Once);

        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "patient-user-123",
            "Upcoming Appointment Reminder",
            It.Is<string>(m => m.Contains("Dr. Magdi Yacoub"))), Times.Once);

        _appointmentRepoMock.Verify(r => r.Update(appt), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task TwilioSmsService_SimulationMode_ReturnsTrue()
    {
        // Arrange
        var options = Options.Create(new TwilioSettings { Enabled = false });
        var loggerMock = new Mock<ILogger<TwilioSmsService>>();
        var service = new TwilioSmsService(options, loggerMock.Object);

        // Act
        var result = await service.SendSmsAsync("+201001234567", "Test simulation");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendGridEmailService_SimulationMode_ReturnsTrue()
    {
        // Arrange
        var options = Options.Create(new SendGridSettings { Enabled = false });
        var loggerMock = new Mock<ILogger<SendGridEmailService>>();
        var service = new SendGridEmailService(options, loggerMock.Object);

        // Act
        var result = await service.SendEmailAsync("test@example.com", "Test Subject", "<p>Test Body</p>");

        // Assert
        result.Should().BeTrue();
    }
}
