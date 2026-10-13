using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
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
    public async Task ProcessCheckoutAsync_WhenAlreadyPaid_ReturnsExistingReceiptIdempotently()
    {
        var appointment = new Appointment
        {
            Id = 1,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 250m,
            PaidAmount = 250m,
            TransactionReference = "TXN-20261010120000-0001",
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

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TransactionReference.Should().Be("TXN-20261010120000-0001");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
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

    [Fact]
    public async Task ProcessCheckoutAsync_WithPromoCode_PersistsPaymentFieldsOnAppointment()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 88,
            ConsultationFee = 300m,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Hassan Test" }
            },
            Doctor = new Doctor
            {
                Id = 2,
                UserId = "doc-test",
                User = new ApplicationUser { FullName = "Doctor Test" },
                Specialization = new Specialization { Name = "Cardiology" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(88)).ReturnsAsync(appointment);
        _mockUow.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 88,
            CardNumber = "4242424242424242",
            CardHolderName = "Hassan Test",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
        appointment.PaidAmount.Should().Be(240m); // 300 - 20% (60)
        appointment.DiscountAmount.Should().Be(60m);
        appointment.PromoCode.Should().Be("DEPI2026");
        appointment.TransactionReference.Should().NotBeNullOrWhiteSpace();
        appointment.TransactionReference.Should().StartWith("TXN-");
        appointment.PaidAt.Should().NotBeNull();
        result.Value!.AmountPaid.Should().Be(240m);
        result.Value.DiscountAmount.Should().Be(60m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task GetReceiptAsync_AfterDiscountedPayment_ReturnsPersistedValuesAccurately()
    {
        // Arrange
        var paidAtUtc = new DateTime(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = 99,
            ConsultationFee = 500m,
            PaidAmount = 400m,
            DiscountAmount = 100m,
            PromoCode = "DEPI2026",
            TransactionReference = "TXN-20261005143000-0099",
            PaidAt = paidAtUtc,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Patient Name" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-owner",
                User = new ApplicationUser { FullName = "Doctor Name" },
                Specialization = new Specialization { Name = "Surgery" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(99)).ReturnsAsync(appointment);

        // Act
        var result = await _sut.GetReceiptAsync(99, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(400m, "Receipt must show discounted amount that was paid, not consultation fee");
        result.Value.OriginalAmount.Should().Be(500m);
        result.Value.DiscountAmount.Should().Be(100m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
        result.Value.TransactionReference.Should().Be("TXN-20261005143000-0099");
        result.Value.PaidAt.Should().Be(paidAtUtc);
    }

    [Fact]
    public async Task GetReceiptAsync_ReceiptValuesRemainImmutable_WhenAppointmentStatusAndUpdatedAtChange()
    {
        // Arrange
        var initialPaidAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var initialTxnRef = "TXN-20261001100000-0101";

        var appointment = new Appointment
        {
            Id = 101,
            ConsultationFee = 250m,
            PaidAmount = 200m,
            DiscountAmount = 50m,
            PromoCode = "MEDICARE50",
            TransactionReference = initialTxnRef,
            PaidAt = initialPaidAt,
            PaymentStatus = PaymentStatus.Paid,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = initialPaidAt,
            UpdatedAt = initialPaidAt,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Patient" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-owner",
                User = new ApplicationUser { FullName = "Doctor" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(101)).ReturnsAsync(appointment);

        // Act 1: Initial receipt
        var receipt1 = await _sut.GetReceiptAsync(101, "pat-owner");

        // Simulate doctor completing the visit a week later
        appointment.Status = AppointmentStatus.Completed;
        appointment.UpdatedAt = initialPaidAt.AddDays(7);

        // Act 2: Subsequent receipt after completion
        var receipt2 = await _sut.GetReceiptAsync(101, "pat-owner");

        // Assert
        receipt1.IsSuccess.Should().BeTrue();
        receipt2.IsSuccess.Should().BeTrue();
        receipt2.Value!.TransactionReference.Should().Be(initialTxnRef, "Transaction reference must remain immutable");
        receipt2.Value.PaidAt.Should().Be(initialPaidAt, "PaidAt timestamp must not shift with UpdatedAt");
        receipt2.Value.AmountPaid.Should().Be(200m);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_AlreadyPaidAppointment_IsIdempotentAndReturnsExistingReceiptWithoutRecharging()
    {
        // Arrange
        var paidAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = 102,
            ConsultationFee = 300m,
            PaidAmount = 240m,
            DiscountAmount = 60m,
            PromoCode = "DEPI2026",
            TransactionReference = "TXN-20261008120000-0102",
            PaidAt = paidAtUtc,
            PaymentStatus = PaymentStatus.Paid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Ali" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Mona" },
                Specialization = new Specialization { Name = "Dental" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(102)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 102,
            CardNumber = "4242424242424242",
            CardHolderName = "Ali",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act: Patient accidentally submits payment form twice
        var result = await _sut.ProcessCheckoutAsync(request, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue("Duplicate checkout must be idempotent and return existing receipt");
        result.Value!.TransactionReference.Should().Be("TXN-20261008120000-0102");
        result.Value.AmountPaid.Should().Be(240m);
        _mockUow.Verify(u => u.CommitAsync(), Times.Never, "Duplicate payment request must not execute a secondary database commit or charge");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ParallelConcurrentCheckout_HandlesConcurrencyIdempotently()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 103,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Concurrent Patient" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Concurrency" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(103)).ReturnsAsync(appointment);

        // First commit succeeds, simulated concurrent commit throws DbUpdateConcurrencyException
        int commitCalls = 0;
        _mockUow.Setup(u => u.CommitAsync()).Returns(() =>
        {
            var callIndex = Interlocked.Increment(ref commitCalls);
            if (callIndex == 1)
            {
                return Task.FromResult(1);
            }
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Row updated concurrently.");
        });

        var request1 = new PaymentCheckoutRequestDto
        {
            AppointmentId = 103,
            CardNumber = "4242424242424242",
            CardHolderName = "Concurrent Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var request2 = new PaymentCheckoutRequestDto
        {
            AppointmentId = 103,
            CardNumber = "4242424242424242",
            CardHolderName = "Concurrent Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act: Execute both concurrent requests simultaneously
        var task1 = _sut.ProcessCheckoutAsync(request1, "pat-owner");
        var task2 = _sut.ProcessCheckoutAsync(request2, "pat-owner");
        var results = await Task.WhenAll(task1, task2);

        // Assert: Both succeed, neither crashes, both return the valid receipt
        results.All(r => r.IsSuccess).Should().BeTrue("Both concurrent checkout requests must succeed idempotently");
        results[0].Value!.TransactionReference.Should().Be(results[1].Value!.TransactionReference);
    }

    [Fact]
    public async Task InitiateAppointmentPaymentAsync_CalculatesAuthoritativeAmountAndPromoCode_ReturnsHostedCheckoutUrl()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 55,
            ConsultationFee = 500m,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { Id = 3, UserId = "patient-55" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(55))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentInitiationResultDto>.Success(new PaymentInitiationResultDto
            {
                Success = true,
                PaymentToken = "paymob_token_123",
                CheckoutUrl = "https://accept.paymob.com/api/acceptance/iframes/12345?payment_token=paymob_token_123",
                TransactionReference = "TXN-APP55-001"
            }));

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        // Act: Use DEPI2026 promo code (20% discount on 500 = 100 discount, payable = 400)
        var result = await service.InitiateAppointmentPaymentAsync(55, "DEPI2026", "patient-55");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Amount.Should().Be(400m, "Server must authoritatively calculate 500 - 20% discount = 400 EGP");
        result.Value.CheckoutUrl.Should().Contain("paymob_token_123");
        appointment.PaymentStatus.Should().Be(PaymentStatus.Pending);
        appointment.DiscountAmount.Should().Be(100m);
        appointment.PromoCode.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task InitiateAppointmentPaymentAsync_IdorAttemptByDifferentUser_ReturnsForbidden()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 55,
            ConsultationFee = 500m,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient { Id = 3, UserId = "legitimate-patient" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(55))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        // Act - Attacker attempts to initiate payment
        var result = await service.InitiateAppointmentPaymentAsync(55, null, "attacker-user-id");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        mockGateway.Verify(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookCallbackAsync_WithValidHmac_TransitionsStatusToPaidAndSendsNotification()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 77,
            ConsultationFee = 300m,
            DiscountAmount = 50m,
            PaymentStatus = PaymentStatus.Pending,
            Patient = new Patient { Id = 5, UserId = "patient-77", User = new ApplicationUser { FullName = "Omar Khaled" } },
            Doctor = new Doctor { Id = 2, User = new ApplicationUser { FullName = "Dr. Samy" }, Specialization = new Specialization { Name = "Cardiology" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(77))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.ValidateWebhookHmac(It.IsAny<PaymobTransactionCallbackDto>(), "valid_hmac_secret"))
            .Returns(true);

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 998811,
            Success = true,
            AmountCents = 25000, // 250.00 EGP (300 - 50 discount)
            Currency = "EGP",
            MerchantOrderId = "ORDER-APP77-123456"
        };

        // Act
        var result = await service.ProcessWebhookCallbackAsync(callback, "valid_hmac_secret");

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
        appointment.PaidAmount.Should().Be(250m);
        appointment.TransactionReference.Should().Be("PAYMOB-998811");
        _mockNotif.Verify(n => n.SendNotificationAsync("patient-77", "Payment Confirmed", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookCallbackAsync_WithInvalidHmac_RejectsAndReturnsFailure()
    {
        // Arrange
        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.ValidateWebhookHmac(It.IsAny<PaymobTransactionCallbackDto>(), It.IsAny<string>()))
            .Returns(false); // HMAC check fails

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 12345,
            Success = true,
            AmountCents = 30000,
            Currency = "EGP",
            MerchantOrderId = "ORDER-APP77"
        };

        // Act
        var result = await service.ProcessWebhookCallbackAsync(callback, "tampered_signature");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid HMAC");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookCallbackAsync_Idempotency_DuplicateWebhookReturnsReceiptWithoutDoubleProcessing()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 88,
            ConsultationFee = 400m,
            PaymentStatus = PaymentStatus.Paid, // ALREADY PAID
            PaidAmount = 400m,
            TransactionReference = "PAYMOB-554433",
            Patient = new Patient { Id = 6, UserId = "patient-88", User = new ApplicationUser { FullName = "Hoda" } },
            Doctor = new Doctor { Id = 1, User = new ApplicationUser { FullName = "Dr. Mona" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(88))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.ValidateWebhookHmac(It.IsAny<PaymobTransactionCallbackDto>(), It.IsAny<string>()))
            .Returns(true);

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 554433,
            Success = true,
            AmountCents = 40000,
            Currency = "EGP",
            MerchantOrderId = "ORDER-APP88"
        };

        // Act - Duplicate webhook received
        var result = await service.ProcessWebhookCallbackAsync(callback, "valid_hmac");

        // Assert
        result.IsSuccess.Should().BeTrue("Duplicate webhook delivery must return existing receipt idempotently");
        result.Value!.TransactionReference.Should().Be("PAYMOB-554433");
        _mockNotif.Verify(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never,
            "Duplicate notification must not be dispatched for already processed payment");
    }

    [Fact]
    public async Task ProcessWebhookCallbackAsync_AmountMismatch_RejectsTamperedPayment()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 99,
            ConsultationFee = 500m,
            PaymentStatus = PaymentStatus.Pending,
            Patient = new Patient { Id = 7, UserId = "patient-99" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(99))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.ValidateWebhookHmac(It.IsAny<PaymobTransactionCallbackDto>(), It.IsAny<string>()))
            .Returns(true);

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 777,
            Success = true,
            AmountCents = 1000, // Attacker paid 10.00 EGP instead of 500.00 EGP
            Currency = "EGP",
            MerchantOrderId = "ORDER-APP99"
        };

        // Act
        var result = await service.ProcessWebhookCallbackAsync(callback, "valid_hmac");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("amount does not match");
        appointment.PaymentStatus.Should().Be(PaymentStatus.Pending, "Appointment must not be marked Paid when amount mismatches");
    }

    [Fact]
    public async Task ProcessWebhookCallbackAsync_WhenGatewayReportsFailure_MarksStatusFailed()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 101,
            ConsultationFee = 350m,
            PaymentStatus = PaymentStatus.Pending,
            Patient = new Patient { Id = 8, UserId = "patient-101" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(101))
            .ReturnsAsync(appointment);

        var mockGateway = new Mock<IPaymentGateway>();
        mockGateway.Setup(g => g.ValidateWebhookHmac(It.IsAny<PaymobTransactionCallbackDto>(), It.IsAny<string>()))
            .Returns(true);

        var service = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, mockGateway.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 888,
            Success = false, // Declined card at gateway
            AmountCents = 35000,
            Currency = "EGP",
            MerchantOrderId = "ORDER-APP101"
        };

        // Act
        var result = await service.ProcessWebhookCallbackAsync(callback, "valid_hmac");

        // Assert
        result.IsSuccess.Should().BeFalse();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Failed);
    }
}
