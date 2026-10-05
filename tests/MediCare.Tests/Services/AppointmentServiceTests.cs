using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AppointmentServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<ILogger<AppointmentService>> _loggerMock;
    private readonly AppointmentFactory _factory;
    private readonly AppointmentService _service;

    public AppointmentServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _notificationServiceMock = new Mock<INotificationService>();
        _clinicClockMock = new Mock<IClinicClock>();
        _loggerMock = new Mock<ILogger<AppointmentService>>();
        _factory = new AppointmentFactory();

        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);

        // Fixed clinic clock for testing: 2026-11-15 08:00:00 (Sunday)
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 8, 0, 0));
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new AppointmentService(
            _uowMock.Object,
            _factory,
            _notificationServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object);
    }

    private Doctor CreateValidDoctor(int doctorId = 1)
    {
        return new Doctor
        {
            Id = doctorId,
            UserId = "doc_user_1",
            IsApproved = true,
            ConsultationFee = 300,
            SlotDurationMinutes = 30,
            User = new ApplicationUser { Id = "doc_user_1", FullName = "Dr. Ahmed Mahmoud" },
            WorkingHours = new List<WorkingHours>
            {
                new()
                {
                    DoctorId = doctorId,
                    DayOfWeek = DayOfWeek.Sunday,
                    StartTime = new TimeSpan(9, 0, 0),
                    EndTime = new TimeSpan(17, 0, 0)
                }
            },
            Leaves = new List<DoctorLeave>()
        };
    }

    private Patient CreateValidPatient(int patientId = 1)
    {
        return new Patient
        {
            Id = patientId,
            UserId = "pat_user_1",
            User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled" }
        };
    }

    [Fact]
    public async Task BookAppointment_ShouldSucceed_WhenValidRequest()
    {
        // Arrange: Valid appointment at 10:00 AM on 2026-11-15 (Sunday)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation,
            Notes = "Checkup"
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _uowMock.Verify(u => u.Appointments.AddAsync(It.Is<Appointment>(a =>
            a.DoctorId == 1 &&
            a.PatientId == 1 &&
            a.Status == AppointmentStatus.Pending &&
            a.ConsultationFee == 300 &&
            a.PaymentStatus == PaymentStatus.Unpaid)), Times.Once);
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "doc_user_1",
            "New Appointment Request",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDoctorNotApproved()
    {
        // Arrange: Doctor is not approved
        var doctor = CreateValidDoctor();
        doctor.IsApproved = false;

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("pending administrative approval");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenLeadTimeUnder30Minutes()
    {
        // Arrange: Clock is 08:00, booking at 08:20 (only 20 min in advance)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(8, 20, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("at least 30 minutes in advance");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDoctorOnLeave()
    {
        // Arrange: Doctor has leave on Nov 15
        var doctor = CreateValidDoctor();
        doctor.Leaves.Add(new DoctorLeave
        {
            StartDate = new DateTime(2026, 11, 15),
            EndDate = new DateTime(2026, 11, 15),
            Reason = "Conference"
        });
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor is on leave");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenOutsideDoctorWorkingHours()
    {
        // Arrange: Doctor works 09:00 - 17:00, booking requested at 18:00
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(18, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("outside the doctor's scheduled clinic hours");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenPatientHasDoubleBooking()
    {
        // Arrange: Patient already booked at same time
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already have an active appointment scheduled");
    }

    [Fact]
    public async Task BookAppointment_ShouldCatchDbUpdateException_AndReturnFriendlyConflictMessage()
    {
        // Arrange: DbUpdateException thrown during commit (Filtered Unique Index violation simulation)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);

        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateException("Violation of unique index", (Exception)null!));

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("This slot was just booked by another patient");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldTransitionFromPendingToConfirmed_AndNotifyPatient()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        _uowMock.Verify(u => u.Appointments.Update(appointment), Times.Once);
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            "Appointment Confirmed",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldFail_WhenDoctorDoesNotOwnAppointment()
    {
        // Arrange: Doctor 2 attempts to confirm Doctor 1's appointment
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 2);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldFail_WhenStatusIsNotPending()
    {
        // Arrange: Already Confirmed
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot confirm appointment");
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenPatientCancelsMoreThan2HoursInAdvance()
    {
        // Arrange: Clock is 08:00, appointment is at 11:00 (3 hours in advance: 3 > 2)
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(11, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 15)), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenPatientCancelsLessThan2HoursInAdvance()
    {
        // Arrange: Clock is 08:00, appointment is at 09:30 (1.5 hours in advance: 1.5 <= 2)
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(9, 30, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be cancelled less than 2 hours before");
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenAlreadyInTerminalState()
    {
        // Arrange: Status is already Completed
        var appointment = new Appointment
        {
            Id = 15,
            Status = AppointmentStatus.Completed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already Completed");
    }

    [Fact]
    public async Task MarkNoShow_ShouldSucceed_WhenConfirmedAndAfterStartTime()
    {
        // Arrange: Clock is 11:30, appointment was scheduled at 10:00 (started in past)
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 11, 30, 0));

        var appointment = new Appointment
        {
            Id = 20,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(20)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.MarkNoShowAsync(20, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
    }

    [Fact]
    public async Task MarkNoShow_ShouldFail_WhenBeforeScheduledStartTime()
    {
        // Arrange: Clock is 08:00, appointment is at 10:00 (not started yet)
        var appointment = new Appointment
        {
            Id = 20,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(20)).ReturnsAsync(appointment);

        // Act
        var result = await _service.MarkNoShowAsync(20, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("before its scheduled start time");
    }
}
