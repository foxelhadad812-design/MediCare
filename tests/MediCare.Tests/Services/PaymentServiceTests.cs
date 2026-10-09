using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PaymentServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<INotificationService> _mockNotif;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _mockNotif = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<PaymentService>>();
        _sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_InvalidCardNumber_ReturnsFailure()
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "1234",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid card number");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ExpiredCard_ReturnsFailure()
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242", // Valid Luhn length
            CardHolderName = "John Doe",
            ExpiryMonth = "01",
            ExpiryYear = "2020", // Expired
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_IdorAttemptByDifferentUser_ReturnsForbidden()
    {
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 2,
            ConsultationFee = 350,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { Id = 2, UserId = "legitimate-patient-id" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(42))
            .ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 42,
            CardNumber = "4242424242424242",
            CardHolderName = "Attacker Name",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act - Attacker attempts payment
        var result = await _sut.ProcessCheckoutAsync(request, "attacker-user-id");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ValidCardAndOwner_AtomicallyMarksPaidAndReturnsReceipt()
    {
        var patientUser = new ApplicationUser { FullName = "Ali Hassan" };
        var doctorUser = new ApplicationUser { FullName = "Sarah Ibrahim" };
        var spec = new Specialization { Name = "Dermatology" };

        var appointment = new Appointment
        {
            Id = 77,
            DoctorId = 5,
            PatientId = 9,
            AppointmentDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            ConsultationFee = 400,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { Id = 9, UserId = "ali-patient-user", User = patientUser },
            Doctor = new Doctor { Id = 5, UserId = "sarah-doctor-user", User = doctorUser, Specialization = spec }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(77))
            .ReturnsAsync(appointment);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 77,
            CardNumber = "4242424242424242",
            CardHolderName = "Ali Hassan",
            ExpiryMonth = "11",
            ExpiryYear = "2028",
            Cvv = "999"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "ali-patient-user");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AmountPaid.Should().Be(400);
        result.Value.AppointmentId.Should().Be(77);
        result.Value.TransactionReference.Should().StartWith("TXN-");
        result.Value.PatientName.Should().Be("Ali Hassan");
        result.Value.DoctorName.Should().Be("Dr. Sarah Ibrahim");

        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
        _mockUow.Verify(u => u.Appointments.Update(appointment), Times.Once);
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task GetReceiptAsync_UnpaidAppointment_ReturnsFailure()
    {
        var appointment = new Appointment
        {
            Id = 50,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient { UserId = "patient-user" },
            Doctor = new Doctor { UserId = "doctor-user" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(50))
            .ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(50, "patient-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not been completed");
    }

    [Fact]
    public async Task GetReceiptAsync_IdorUnauthorizedUser_ReturnsForbidden()
    {
        var appointment = new Appointment
        {
            Id = 50,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient { UserId = "owner-patient" },
            Doctor = new Doctor { UserId = "doctor-id" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(50))
            .ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(50, "random-stranger");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_ValidCodeDEPI2026_Returns20PercentDiscount()
    {
        // Act
        var result = await _sut.ValidatePromoCodeAsync("depi2026", 100m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DiscountAmount.Should().Be(20m);
        result.Value.FinalAmount.Should().Be(80m);
        result.Value.Code.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_InvalidCode_ReturnsFailure()
    {
        // Act
        var result = await _sut.ValidatePromoCodeAsync("INVALID_CODE_123", 100m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WithValidPromoCode_AppliesDiscount()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 55,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient
            {
                UserId = "pat-1",
                User = new ApplicationUser { FullName = "Mahmoud" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-1",
                User = new ApplicationUser { FullName = "Dr. Tarek" },
                Specialization = new Specialization { Name = "Neurology" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(55)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 55,
            CardNumber = "4242424242424242",
            CardHolderName = "Mahmoud Ali",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "pat-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(160m); // 200 - 20% (40) = 160
        result.Value.OriginalAmount.Should().Be(200m);
        result.Value.DiscountAmount.Should().Be(40m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_NullRequest_ReturnsFailure()
    {
        var result = await _sut.ProcessCheckoutAsync(null!, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ProcessCheckoutAsync_InvalidAppointmentId_ReturnsFailure(int apptId)
    {
        var request = new PaymentCheckoutRequestDto { AppointmentId = apptId };
        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Valid appointment ID is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task ProcessCheckoutAsync_InvalidCardholderName_ReturnsFailure(string? name)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = name!,
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cardholder name is required");
    }

    [Theory]
    [InlineData("13", "2029", "month")]
    [InlineData("0", "2029", "month")]
    [InlineData("invalid", "2029", "month")]
    [InlineData("12", "invalid", "year")]
    public async Task ProcessCheckoutAsync_InvalidExpiryDates_ReturnsFailure(string month, string year, string errorField)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = month,
            ExpiryYear = year,
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.ToLower().Should().Contain(errorField);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    public async Task ProcessCheckoutAsync_InvalidCvv_ReturnsFailure(string? cvv)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = cvv!
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid CVV");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenAppointmentNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 999,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    public async Task ProcessCheckoutAsync_WhenCancelledOrRejected_ReturnsFailure(AppointmentStatus status)
    {
        var appointment = new Appointment
        {
            Id = 1,
            Status = status,
            Patient = new Patient { UserId = "user-1" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot process payment for a cancelled or rejected");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenAlreadyPaid_ReturnsFailure()
    {
        var appointment = new Appointment
        {
            Id = 1,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient { UserId = "user-1" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been paid");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_TwoDigitYear_ParsesCorrectlyAndSucceeds()
    {
        var appointment = new Appointment
        {
            Id = 1,
            ConsultationFee = 300,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { UserId = "user-1", User = new ApplicationUser { FullName = "User 1" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "29", // Two digit year
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessCheckoutAsync_NotificationException_DoesNotFailPayment()
    {
        var appointment = new Appointment
        {
            Id = 1,
            ConsultationFee = 300,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { UserId = "user-1", User = new ApplicationUser { FullName = "User 1" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);
        _mockNotif.Setup(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Notification gateway network error"));

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeTrue();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetReceiptAsync_InvalidAppointmentId_ReturnsFailure(int id)
    {
        var result = await _sut.GetReceiptAsync(id, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Valid appointment ID is required");
    }

    [Fact]
    public async Task GetReceiptAsync_AppointmentNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(100)).ReturnsAsync((Appointment?)null);

        var result = await _sut.GetReceiptAsync(100, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task GetReceiptAsync_WhenUserIsDoctor_ReturnsSuccess()
    {
        var appointment = new Appointment
        {
            Id = 1,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 450,
            Patient = new Patient { UserId = "pat-10", User = new ApplicationUser { FullName = "Patient Ten" } },
            Doctor = new Doctor { UserId = "doc-20", User = new ApplicationUser { FullName = "Doctor Twenty" } },
            CreatedAt = new DateTime(2026, 10, 1)
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(1, "doc-20");

        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(450);
        result.Value.DoctorName.Should().Be("Dr. Doctor Twenty");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePromoCodeAsync_EmptyOrWhitespace_ReturnsFailure(string? code)
    {
        var result = await _sut.ValidatePromoCodeAsync(code!, 200m);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("enter a valid promo code");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_Medicare50_Applies50Discount()
    {
        var result = await _sut.ValidatePromoCodeAsync("medicare50", 200m);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiscountAmount.Should().Be(50m);
        result.Value.FinalAmount.Should().Be(150m);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_Welcome10_Applies10PercentDiscount()
    {
        var result = await _sut.ValidatePromoCodeAsync("welcome10", 300m);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiscountAmount.Should().Be(30m);
        result.Value.FinalAmount.Should().Be(270m);
    }
}
