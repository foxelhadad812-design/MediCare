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
}
