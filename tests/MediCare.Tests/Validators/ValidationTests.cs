using FluentAssertions;
using MediCare.Services.DTOs;
using MediCare.Services.Validators;
using Xunit;

namespace MediCare.Tests.Validators;

public class ValidationTests
{
    private readonly PatientRegisterValidator _patientValidator = new();
    private readonly DoctorRegisterValidator _doctorValidator = new();

    [Fact]
    public void PatientRegisterValidator_ShouldFail_WhenPasswordDoesNotMeetComplexity()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Ahmed",
            Email = "ahmed@clinic.com",
            Password = "simple", // Missing upper, digit, special, and min length
            ConfirmPassword = "simple",
            DateOfBirth = DateTime.Today.AddYears(-20),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void PatientRegisterValidator_ShouldFail_WhenBirthDateIsInFuture()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Ahmed",
            Email = "ahmed@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            DateOfBirth = DateTime.Today.AddDays(5),
            Gender = "Male"
        };

        // Act
        var result = _patientValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DateOfBirth");
    }

    [Fact]
    public void DoctorRegisterValidator_ShouldFail_WhenSpecializationIdIsZero()
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = "Dr. Tarek",
            Email = "tarek@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 0,
            LicenseNumber = "LIC-1234",
            ConsultationFee = 200
        };

        // Act
        var result = _doctorValidator.Validate(dto);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SpecializationId");
    }
}
