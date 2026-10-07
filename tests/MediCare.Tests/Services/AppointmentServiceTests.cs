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
    private readonly Mock<IEmailService> _emailServiceMock;
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
        _emailServiceMock = new Mock<IEmailService>();
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
            _loggerMock.Object,
            _emailServiceMock.Object);
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
    public async Task CancelAppointment_ShouldFail_WhenDifferentDoctorAttemptsCancel_WithoutAdminRole()
    {
        // Arrange: Appointment owned by Doctor 1 (UserId: "doc_user_1")
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act: Doctor 2 (UserId: "other_doc_user") attempts to cancel
        var result = await _service.CancelAppointmentAsync(15, "other_doc_user", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenOwningDoctorCancels()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Owning doctor cancels
        var result = await _service.CancelAppointmentAsync(15, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenAdminCancelsAnyDoctorAppointment()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Admin (different UserId) cancels with isAdmin = true
        var result = await _service.CancelAppointmentAsync(15, "admin_user_99", isDoctorOrAdmin: true, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
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

    [Fact]
    public async Task GetAppointmentById_ShouldReturnSuccess_WhenFound()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(14, 30, 0),
            ConsultationFee = 350,
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.GetAppointmentByIdAsync(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.DoctorName.Should().Be("Dr. Ahmed Mahmoud");
        result.Value.PatientName.Should().Be("Omar Khaled");
        result.Value.CanCancel.Should().BeTrue();
    }

    [Fact]
    public async Task GetAppointmentById_ShouldReturnFailure_WhenNotFound()
    {
        // Arrange
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999)).ReturnsAsync((Appointment?)null);

        // Act
        var result = await _service.GetAppointmentByIdAsync(999);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task BookAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
    {
        // Arrange
        var doctor = CreateValidDoctor();
        var patient = new Patient
        {
            Id = 1,
            UserId = "pat_user_1",
            User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
        };

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(false);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Received")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
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
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat_user_1",
                User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
            }
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Confirmed")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(11, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat_user_1",
                User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
            }
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Cancelled")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenValidAndWithinRules()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0), // 6h away from 08:00
            EndTime = new TimeSpan(14, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 10,
            NewAppointmentDate = new DateTime(2026, 11, 22), // Sunday
            NewStartTime = new TimeSpan(10, 0, 0),
            Reason = "Schedule adjustment"
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        appointment.StartTime.Should().Be(new TimeSpan(10, 0, 0));
        appointment.Notes.Should().Contain("Rescheduled: Schedule adjustment");
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 15)), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 22)), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenLessThan2HoursFromCurrentStart()
    {
        // Arrange: appointment starts at 09:30, clock is 08:00 (1.5 hours difference)
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 11,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(9, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(11)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 11,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("less than 2 hours before the current scheduled start time");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenPatientDoesNotOwnAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 12,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(12)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 12,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Different user tries to reschedule
        var result = await _service.RescheduleAppointmentAsync(dto, "other_unauthorized_user", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden: You can only reschedule your own appointments.");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewSlotHasConflict()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        // Conflict on new slot
        var conflictingAppt = new Appointment
        {
            Id = 99,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { conflictingAppt });

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already booked");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenDifferentDoctorAttemptsReschedule_WithoutAdminRole()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Doctor 2 (UserId: "other_doc_user") attempts to reschedule
        var result = await _service.RescheduleAppointmentAsync(dto, "other_doc_user", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenOwningDoctorReschedules()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Owning doctor reschedules
        var result = await _service.RescheduleAppointmentAsync(dto, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenAdminReschedulesAnyDoctorAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Admin (UserId: "admin_user_99") reschedules with isAdmin = true
        var result = await _service.RescheduleAppointmentAsync(dto, "admin_user_99", isDoctorOrAdmin: true, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldSucceed_WhenDoctorOwnsAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "doc_user_1", isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("دورك")),
            It.IsAny<string>()), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifyAppointmentStatusChangedAsync(42, "Calling", "pat_user_1"), Times.Once);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenDoctorDoesNotOwnAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "different_doctor_user", isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldSucceed_WhenAdminCalls()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "admin_user", isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("دورك")),
            It.IsAny<string>()), Times.Once);
    }
}
