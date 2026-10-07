using FluentAssertions;
using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class DoctorServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly IValidator<DoctorFilterDto> _validator;
    private readonly DoctorService _sut;

    public DoctorServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _validator = new DoctorFilterValidator();
        _sut = new DoctorService(_mockUow.Object, _validator);
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithNoFilters_ReturnsApprovedDoctorsAndTotalCount()
    {
        // Arrange
        var testDoctors = new List<Doctor>
        {
            new()
            {
                Id = 1,
                SpecializationId = 1,
                ConsultationFee = 300,
                IsApproved = true,
                User = new ApplicationUser { FullName = "Dr. Ahmed Mahmoud" },
                Specialization = new Specialization { Id = 1, Name = "Cardiology" },
                WorkingHours = new List<WorkingHours>
                {
                    new() { DayOfWeek = DayOfWeek.Sunday }
                }
            },
            new()
            {
                Id = 2,
                SpecializationId = 2,
                ConsultationFee = 250,
                IsApproved = true,
                User = new ApplicationUser { FullName = "Dr. Sara Al-Sayed" },
                Specialization = new Specialization { Id = 2, Name = "Dermatology" },
                WorkingHours = new List<WorkingHours>
                {
                    new() { DayOfWeek = DayOfWeek.Monday }
                }
            }
        };

        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, null, 1, 6))
            .ReturnsAsync((testDoctors, 2));

        var filter = new DoctorFilterDto();

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.First().FullName.Should().Be("Dr. Ahmed Mahmoud");
        result.Items.First().SpecializationName.Should().Be("Cardiology");
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithSpecializationFilter_CallsRepositoryWithParameter()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                1, null, null, null, null, 1, 6))
            .ReturnsAsync((new List<Doctor>(), 0));

        var filter = new DoctorFilterDto { SpecializationId = 1 };

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        _mockUow.Verify(u => u.Doctors.SearchApprovedDoctorsAsync(
            1, null, null, null, null, 1, 6), Times.Once);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchDoctorsAsync_WithGovernorateFilter_CallsRepositoryWithGovernorateParameter()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.SearchApprovedDoctorsAsync(
                null, null, null, null, "Alexandria", 1, 6))
            .ReturnsAsync((new List<Doctor>(), 0));

        var filter = new DoctorFilterDto { Governorate = "Alexandria" };

        // Act
        var result = await _sut.SearchDoctorsAsync(filter);

        // Assert
        _mockUow.Verify(u => u.Doctors.SearchApprovedDoctorsAsync(
            null, null, null, null, "Alexandria", 1, 6), Times.Once);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDoctorDetailsAsync_WhenDoctorExistsAndApproved_ReturnsSuccessWithMappedDto()
    {
        // Arrange
        var doctor = new Doctor
        {
            Id = 5,
            SpecializationId = 3,
            LicenseNumber = "LIC-9988",
            ConsultationFee = 350,
            SlotDurationMinutes = 30,
            Bio = "Senior Consultant in Orthopedic Surgery.",
            IsApproved = true,
            User = new ApplicationUser
            {
                FullName = "Dr. Mona Mansour",
                Email = "mona@clinic.com",
                PhoneNumber = "+201012345678"
            },
            Specialization = new Specialization { Id = 3, Name = "Orthopedics" },
            WorkingHours = new List<WorkingHours>
            {
                new() { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(20, 0, 0) }
            }
        };

        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(5))
            .ReturnsAsync(doctor);

        // Act
        var result = await _sut.GetDoctorDetailsAsync(5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FullName.Should().Be("Dr. Mona Mansour");
        result.Value.Email.Should().Be("mona@clinic.com");
        result.Value.SpecializationName.Should().Be("Orthopedics");
        result.Value.LicenseNumber.Should().Be("LIC-9988");
        result.Value.WorkingHours.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDoctorDetailsAsync_WhenDoctorNotFound_ReturnsFailure()
    {
        // Arrange
        _mockUow.Setup(u => u.Doctors.GetDoctorWithScheduleAsync(999))
            .ReturnsAsync((Doctor?)null);

        // Act
        var result = await _sut.GetDoctorDetailsAsync(999);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSpecializationsAsync_ReturnsMappedAlphabeticalList()
    {
        // Arrange
        var specs = new List<Specialization>
        {
            new() { Id = 1, Name = "Pediatrics" },
            new() { Id = 2, Name = "Cardiology" }
        };

        _mockUow.Setup(u => u.Specializations.GetAllAsync())
            .ReturnsAsync(specs);

        // Act
        var result = await _sut.GetSpecializationsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Cardiology"); // Sorted alphabetically
        result[1].Name.Should().Be("Pediatrics");
    }
}
