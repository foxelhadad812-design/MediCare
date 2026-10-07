using System.Linq.Expressions;
using FluentAssertions;
using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly IValidator<PatientRegisterDto> _patientValidator;
    private readonly IValidator<DoctorRegisterDto> _doctorValidator;
    private readonly IValidator<LoginDto> _loginValidator;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object, contextAccessor.Object, claimsFactory.Object, null!, null!, null!, null!);

        _mockUow = new Mock<IUnitOfWork>();
        _patientValidator = new PatientRegisterValidator();
        _doctorValidator = new DoctorRegisterValidator();
        _loginValidator = new LoginValidator();

        _sut = new AuthService(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _mockUow.Object,
            _patientValidator,
            _doctorValidator,
            _loginValidator);
    }

    [Fact]
    public async Task RegisterPatientAsync_WithValidDto_CreatesUserAndAssignsPatientRole()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Khaled Omar",
            Email = "khaled@test.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "+201012345678",
            DateOfBirth = new DateTime(1990, 5, 20),
            Gender = "Male",
            BloodGroup = "A+"
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync((ApplicationUser?)null);

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Patient"))
            .ReturnsAsync(IdentityResult.Success);

        _mockUow.Setup(u => u.Patients.AddAsync(It.IsAny<Patient>()))
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RegisterPatientAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockUserManager.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == dto.Email), dto.Password), Times.Once);
        _mockUserManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Patient"), Times.Once);
        _mockUow.Verify(u => u.Patients.AddAsync(It.IsAny<Patient>()), Times.Once);
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), false, null), Times.Once);
    }

    [Fact]
    public async Task RegisterDoctorAsync_WithValidDto_SetsIsApprovedToFalseAndDoesNotSignIn()
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = "Dr. Ahmed Taha",
            Email = "ahmed.taha@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 2,
            LicenseNumber = "LIC-EGY-4455",
            ConsultationFee = 300,
            Bio = "Cardiologist"
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync((ApplicationUser?)null);

        _mockUow.Setup(u => u.Specializations.GetByIdAsync(2))
            .ReturnsAsync(new Specialization { Id = 2, Name = "Cardiology" });

        _mockUow.Setup(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor>());

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Doctor"))
            .ReturnsAsync(IdentityResult.Success);

        Doctor? capturedDoctor = null;
        _mockUow.Setup(u => u.Doctors.AddAsync(It.IsAny<Doctor>()))
            .Callback<Doctor>(d => capturedDoctor = d)
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RegisterDoctorAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedDoctor.Should().NotBeNull();
        capturedDoctor!.IsApproved.Should().BeFalse("new doctors must require administrator approval");
        capturedDoctor.LicenseNumber.Should().Be("LIC-EGY-4455");

        // Doctor must NOT be signed in automatically
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenDoctorIsNotApproved_ReturnsPendingApprovalMessage()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "pending.doctor@clinic.com",
            Password = "P@ssword123!"
        };

        var user = new ApplicationUser { Id = "doc-guid-1", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.IsInRoleAsync(user, "Doctor"))
            .ReturnsAsync(true);

        _mockUow.Setup(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { new() { UserId = user.Id, IsApproved = false } });

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("pending administrator approval");
        _mockSignInManager.Verify(s => s.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetPatientProfileAsync_WhenUserAndPatientExist_ReturnsSuccessWithProfileDto()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var user = new ApplicationUser
        {
            Id = userId,
            FullName = "Khaled Omar",
            Email = "khaled@medicare.com",
            PhoneNumber = "+201099112233"
        };
        var patient = new Patient
        {
            Id = 42,
            UserId = userId,
            DateOfBirth = new DateTime(1990, 5, 20),
            Gender = "Male",
            BloodGroup = "O+",
            EmergencyContact = "+201099112234",
            Allergies = "Penicillin",
            MedicalHistory = "Mild hypertension"
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId))
            .ReturnsAsync(user);

        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        // Act
        var result = await _sut.GetPatientProfileAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.FullName.Should().Be("Khaled Omar");
        result.Value.Email.Should().Be("khaled@medicare.com");
        result.Value.BloodGroup.Should().Be("O+");
        result.Value.Allergies.Should().Be("Penicillin");
        result.Value.MedicalHistory.Should().Be("Mild hypertension");
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WithValidDto_UpdatesIdentityAndPatientEntities()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var user = new ApplicationUser
        {
            Id = userId,
            FullName = "Old Name",
            PhoneNumber = "+201000000000"
        };
        var patient = new Patient
        {
            Id = 42,
            UserId = userId,
            DateOfBirth = new DateTime(1990, 1, 1),
            Gender = "Male",
            BloodGroup = "A+",
            Allergies = null,
            MedicalHistory = null
        };

        var updateDto = new PatientUpdateProfileDto
        {
            FullName = "Updated Name",
            PhoneNumber = "+201011112222",
            DateOfBirth = new DateTime(1992, 3, 15),
            Gender = "Male",
            BloodGroup = "B+",
            EmergencyContact = "+201099998888",
            Allergies = "Aspirin, Peanuts",
            MedicalHistory = "Asthma diagnosed 2021"
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.UpdatePatientProfileAsync(userId, updateDto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.FullName.Should().Be("Updated Name");
        user.PhoneNumber.Should().Be("+201011112222");
        patient.BloodGroup.Should().Be("B+");
        patient.Allergies.Should().Be("Aspirin, Peanuts");
        patient.MedicalHistory.Should().Be("Asthma diagnosed 2021");

        _mockUow.Verify(u => u.Patients.Update(patient), Times.Once);
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WithInvalidDobInFuture_ReturnsValidationError()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var updateDto = new PatientUpdateProfileDto
        {
            FullName = "Valid Name",
            DateOfBirth = DateTime.Today.AddDays(5), // Future date
            Gender = "Male"
        };

        // Act
        var result = await _sut.UpdatePatientProfileAsync(userId, updateDto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Date of Birth must be in the past");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }
}
