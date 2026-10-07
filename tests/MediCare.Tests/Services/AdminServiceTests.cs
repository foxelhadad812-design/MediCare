using System.Linq.Expressions;
using System.Text;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<IRepository<Specialization>> _specRepoMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<ILogger<AdminService>> _loggerMock;
    private readonly AdminService _service;

    public AdminServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _specRepoMock = new Mock<IRepository<Specialization>>();
        _emailServiceMock = new Mock<IEmailService>();
        _clinicClockMock = new Mock<IClinicClock>();
        _loggerMock = new Mock<ILogger<AdminService>>();

        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);
        _uowMock.Setup(u => u.Specializations).Returns(_specRepoMock.Object);

        // Fixed clock for testing: 2026-11-15
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPendingDoctorsAsync_ReturnsOnlyUnapprovedDoctors()
    {
        // Arrange
        var unapprovedDoctors = new List<Doctor>
        {
            new Doctor { Id = 10, UserId = "u1", IsApproved = false, LicenseNumber = "MD-001" },
            new Doctor { Id = 11, UserId = "u2", IsApproved = false, LicenseNumber = "MD-002" }
        };

        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(unapprovedDoctors);

        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10))
            .ReturnsAsync(new Doctor
            {
                Id = 10,
                UserId = "u1",
                IsApproved = false,
                LicenseNumber = "MD-001",
                User = new ApplicationUser { FullName = "Dr. Pend One", Email = "one@med.com" }
            });

        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(11))
            .ReturnsAsync(new Doctor
            {
                Id = 11,
                UserId = "u2",
                IsApproved = false,
                LicenseNumber = "MD-002",
                User = new ApplicationUser { FullName = "Dr. Pend Two", Email = "two@med.com" }
            });

        // Act
        var result = await _service.GetPendingDoctorsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value!.Select(d => d.LicenseNumber).Should().Contain(new[] { "MD-001", "MD-002" });
    }

    [Fact]
    public async Task ApproveDoctorAsync_UnapprovedDoctor_MarksApprovedAndSendsEmail()
    {
        // Arrange
        var doc = new Doctor
        {
            Id = 10,
            IsApproved = false,
            User = new ApplicationUser { Email = "dr.new@test.com", FullName = "Dr. New" }
        };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10)).ReturnsAsync(doc);

        // Act
        var result = await _service.ApproveDoctorAsync(10);

        // Assert
        result.IsSuccess.Should().BeTrue();
        doc.IsApproved.Should().BeTrue();
        _doctorRepoMock.Verify(r => r.Update(doc), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "dr.new@test.com",
            It.Is<string>(s => s.Contains("Approved")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ApproveDoctorAsync_AlreadyApprovedDoctor_ReturnsFailure()
    {
        // Arrange
        var doc = new Doctor { Id = 10, IsApproved = true };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10)).ReturnsAsync(doc);

        // Act
        var result = await _service.ApproveDoctorAsync(10);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already approved");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RejectDoctorAsync_UnapprovedDoctor_DeletesDoctorAndSendsRejectionEmail()
    {
        // Arrange
        var doc = new Doctor
        {
            Id = 15,
            IsApproved = false,
            User = new ApplicationUser { Email = "declined@med.com", FullName = "Dr. Declined" }
        };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(15)).ReturnsAsync(doc);

        // Act
        var result = await _service.RejectDoctorAsync(15, "Invalid Syndicate ID");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _doctorRepoMock.Verify(r => r.Delete(doc), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "declined@med.com",
            It.Is<string>(s => s.Contains("Application Update")),
            It.Is<string>(b => b.Contains("Invalid Syndicate ID"))), Times.Once);
    }

    [Fact]
    public async Task RejectDoctorAsync_AlreadyApprovedDoctor_ReturnsFailure()
    {
        // Arrange
        var doc = new Doctor { Id = 15, IsApproved = true };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(15)).ReturnsAsync(doc);

        // Act
        var result = await _service.RejectDoctorAsync(15, "Some reason");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot reject an already approved doctor");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_CalculatesCorrectAggregationsAndRevenue()
    {
        // Arrange
        var appointments = new List<Appointment>
        {
            new Appointment { Id = 1, Status = AppointmentStatus.Completed, PaymentStatus = PaymentStatus.Paid, ConsultationFee = 500, AppointmentDate = new DateTime(2026, 11, 10), DoctorId = 1 },
            new Appointment { Id = 2, Status = AppointmentStatus.Completed, PaymentStatus = PaymentStatus.Paid, ConsultationFee = 350, AppointmentDate = new DateTime(2026, 11, 12), DoctorId = 1 },
            new Appointment { Id = 3, Status = AppointmentStatus.Confirmed, PaymentStatus = PaymentStatus.Unpaid, ConsultationFee = 400, AppointmentDate = new DateTime(2026, 11, 14), DoctorId = 2 },
            new Appointment { Id = 4, Status = AppointmentStatus.Cancelled, PaymentStatus = PaymentStatus.Unpaid, ConsultationFee = 300, AppointmentDate = new DateTime(2026, 11, 13), DoctorId = 2 },
            new Appointment { Id = 5, Status = AppointmentStatus.NoShow, PaymentStatus = PaymentStatus.Unpaid, ConsultationFee = 250, AppointmentDate = new DateTime(2026, 11, 13), DoctorId = 1 }
        };

        var doctors = new List<Doctor>
        {
            new Doctor { Id = 1, IsApproved = true, SpecializationId = 1 },
            new Doctor { Id = 2, IsApproved = true, SpecializationId = 2 },
            new Doctor { Id = 3, IsApproved = false, SpecializationId = 1 }
        };

        var specializations = new List<Specialization>
        {
            new Specialization { Id = 1, Name = "Cardiology" },
            new Specialization { Id = 2, Name = "Dermatology" }
        };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appointments);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(doctors);
        _specRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(specializations);

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value!;
        metrics.TotalAppointments.Should().Be(5);
        metrics.ActiveDoctorsCount.Should().Be(2);
        metrics.PendingDoctorsCount.Should().Be(1);
        metrics.CompletedVisitsCount.Should().Be(2);

        // Paid revenue: 500 + 350 = 850
        metrics.TotalRevenueCollected.Should().Be(850m);
        // Pending revenue excludes Cancelled: 400 + 250 = 650
        metrics.TotalPendingRevenue.Should().Be(650m);

        metrics.MonthlyTrends.Should().NotBeEmpty();
        metrics.SpecializationBreakdown.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportAppointmentsCsvAsync_ReturnsUtf8BomWithEscapedColumns()
    {
        // Arrange
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 1,
                AppointmentDate = new DateTime(2026, 11, 15),
                StartTime = new TimeSpan(10, 0, 0),
                DoctorId = 1,
                PatientId = 2,
                Status = AppointmentStatus.Completed,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Paid
            }
        };

        var doc = new Doctor
        {
            Id = 1,
            Specialization = new Specialization { Name = "General, Medicine" }, // Contains comma!
            User = new ApplicationUser { FullName = "Dr. John \"Jack\" Smith" } // Contains quotes!
        };

        var pat = new Patient
        {
            Id = 2,
            User = new ApplicationUser { FullName = "Jane Doe" }
        };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(1)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsCsvAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();

        var bytes = result.Value!;
        // Verify UTF-8 BOM preamble (0xEF, 0xBB, 0xBF)
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csvText = Encoding.UTF8.GetString(bytes);
        csvText.Should().Contain("AppointmentId,Date,Time,Doctor,Specialization,Patient,Status,Fee,PaymentStatus");
        // Escaped quotes: ""Jack""
        csvText.Should().Contain("\"Dr. John \"\"Jack\"\" Smith\"");
        // Escaped commas: "General, Medicine"
        csvText.Should().Contain("\"General, Medicine\"");
        csvText.Should().Contain("Jane Doe");
        csvText.Should().Contain("350.00");
    }

    [Fact]
    public async Task ExportAppointmentsCsvAsync_NeutralizesFormulaInjectionCharacters()
    {
        // Arrange: Patient name starts with '=' and Doctor name starts with '@' (Formula Injection payload)
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 101,
                AppointmentDate = new DateTime(2026, 10, 1),
                StartTime = new TimeSpan(9, 30, 0),
                DoctorId = 5,
                PatientId = 6,
                Status = AppointmentStatus.Confirmed,
                ConsultationFee = 400,
                PaymentStatus = PaymentStatus.Unpaid
            }
        };

        var doc = new Doctor
        {
            Id = 5,
            Specialization = new Specialization { Name = "+Surgery" },
            User = new ApplicationUser { FullName = "@AttackerDoc" }
        };

        var pat = new Patient
        {
            Id = 6,
            User = new ApplicationUser { FullName = "=cmd|' /C calc'!A0" }
        };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(5)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsCsvAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var csvText = Encoding.UTF8.GetString(result.Value!);
        // Leading '=' neutralized to "'="
        csvText.Should().Contain("'=cmd|' /C calc'!A0");
        // Leading '@' neutralized to "'@"
        csvText.Should().Contain("'@AttackerDoc");
        // Leading '+' neutralized to "'+Surgery"
        csvText.Should().Contain("'+Surgery");
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_CalculatesTopDoctorsAndDemographicsCorrectly()
    {
        // Arrange
        var appts = new List<Appointment>
        {
            new Appointment { Id = 1, DoctorId = 1, PatientId = 1, Status = AppointmentStatus.Completed, PaymentStatus = PaymentStatus.Paid, ConsultationFee = 500, AppointmentDate = new DateTime(2026, 11, 1) },
            new Appointment { Id = 2, DoctorId = 1, PatientId = 2, Status = AppointmentStatus.Completed, PaymentStatus = PaymentStatus.Paid, ConsultationFee = 500, AppointmentDate = new DateTime(2026, 11, 2) },
            new Appointment { Id = 3, DoctorId = 2, PatientId = 1, Status = AppointmentStatus.Confirmed, PaymentStatus = PaymentStatus.Paid, ConsultationFee = 300, AppointmentDate = new DateTime(2026, 11, 3) }
        };

        var doc1 = new Doctor { Id = 1, SpecializationId = 10, IsApproved = true, User = new ApplicationUser { FullName = "Dr. Ahmed" } };
        var doc2 = new Doctor { Id = 2, SpecializationId = 20, IsApproved = true, User = new ApplicationUser { FullName = "Dr. Sara" } };

        var spec1 = new Specialization { Id = 10, Name = "Cardiology" };
        var spec2 = new Specialization { Id = 20, Name = "Dermatology" };

        var patient1 = new Patient { Id = 1, Gender = "Male", DateOfBirth = new DateTime(1990, 5, 10), User = new ApplicationUser { FullName = "Omar" } };
        var patient2 = new Patient { Id = 2, Gender = "Female", DateOfBirth = new DateTime(2015, 2, 20), User = new ApplicationUser { FullName = "Lina" } };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc1, doc2 });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(1)).ReturnsAsync(doc1);
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(2)).ReturnsAsync(doc2);
        _specRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Specialization> { spec1, spec2 });
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { patient1, patient2 });

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TopDoctors.Should().HaveCount(2);
        result.Value.TopDoctors[0].DoctorName.Should().Be("Dr. Ahmed");
        result.Value.TopDoctors[0].TotalAppointments.Should().Be(2);
        result.Value.TopDoctors[0].TotalRevenue.Should().Be(1000);

        result.Value.Demographics.TotalPatients.Should().Be(2);
        result.Value.Demographics.MaleCount.Should().Be(1);
        result.Value.Demographics.FemaleCount.Should().Be(1);
        result.Value.Demographics.AgeUnder18Count.Should().Be(1); // Born 2015, clock is 2026 -> 11 yrs
        result.Value.Demographics.Age36To50Count.Should().Be(1);  // Born 1990, clock is 2026 -> 36 yrs
    }

    [Fact]
    public async Task GetPatientsAsync_WithSearchTerm_ReturnsMatchingPatients()
    {
        // Arrange
        var patients = new List<Patient>
        {
            new Patient { Id = 1, UserId = "u1", Gender = "Male", DateOfBirth = new DateTime(1995, 1, 1), Allergies = "Penicillin", User = new ApplicationUser { FullName = "Mostafa Hassan", Email = "mostafa@test.com" } },
            new Patient { Id = 2, UserId = "u2", Gender = "Female", DateOfBirth = new DateTime(2000, 1, 1), Allergies = "None", User = new ApplicationUser { FullName = "Salma Khaled", Email = "salma@test.com" } }
        };

        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(patients);
        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Appointment>());

        // Act
        var result = await _service.GetPatientsAsync("Mostafa");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].FullName.Should().Be("Mostafa Hassan");
        result.Value[0].Allergies.Should().Be("Penicillin");
    }

    [Fact]
    public async Task TogglePatientLockoutAsync_PatientNotFound_ReturnsFailure()
    {
        // Arrange
        _patientRepoMock.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Patient?)null);

        // Act
        var result = await _service.TogglePatientLockoutAsync(999, true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task CreateSpecializationAsync_ValidData_AddsEntityAndReturnsId()
    {
        // Arrange
        var dto = new MediCare.Services.DTOs.CreateSpecializationDto { Name = "Neurology", Description = "Brain and nerves" };
        _specRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Specialization, bool>>>()))
            .ReturnsAsync(new List<Specialization>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.CreateSpecializationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _specRepoMock.Verify(r => r.AddAsync(It.Is<Specialization>(s => s.Name == "Neurology")), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateSpecializationAsync_DuplicateName_ReturnsFailure()
    {
        // Arrange
        var dto = new MediCare.Services.DTOs.CreateSpecializationDto { Name = "Cardiology" };
        _specRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Specialization, bool>>>()))
            .ReturnsAsync(new List<Specialization> { new Specialization { Id = 1, Name = "Cardiology" } });

        // Act
        var result = await _service.CreateSpecializationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task DeleteSpecializationAsync_WithAssignedDoctors_ReturnsFailure()
    {
        // Arrange
        var spec = new Specialization { Id = 5, Name = "Pediatrics" };
        _specRepoMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(spec);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { new Doctor { Id = 10, SpecializationId = 5 } });

        // Act
        var result = await _service.DeleteSpecializationAsync(5);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot delete specialization");
        _specRepoMock.Verify(r => r.Delete(It.IsAny<Specialization>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSpecializationAsync_WithoutAssignedDoctors_DeletesSuccessfully()
    {
        // Arrange
        var spec = new Specialization { Id = 6, Name = "Empty Spec" };
        _specRepoMock.Setup(r => r.GetByIdAsync(6)).ReturnsAsync(spec);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.DeleteSpecializationAsync(6);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _specRepoMock.Verify(r => r.Delete(spec), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }
}
