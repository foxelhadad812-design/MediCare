using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class PaymentApiControllerTests
{
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<ILogger<PaymentApiController>> _loggerMock;
    private readonly PaymentApiController _controller;

    public PaymentApiControllerTests()
    {
        _paymentServiceMock = new Mock<IPaymentService>();
        _loggerMock = new Mock<ILogger<PaymentApiController>>();
        _controller = new PaymentApiController(_paymentServiceMock.Object, _loggerMock.Object);
    }

    private void SetUserContext(string? userId, string role = "Patient")
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task Checkout_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);
        var request = new PaymentCheckoutRequestDto { AppointmentId = 1 };

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Checkout_ShouldReturnBadRequest_WhenServiceFails_DueToInvalidCardOrState()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 10,
            CardHolderName = "Jane Doe",
            CardNumber = "41111111", // Invalid length
            ExpiryMonth = "12",
            ExpiryYear = "2028",
            Cvv = "123"
        };

        _paymentServiceMock.Setup(s => s.ProcessCheckoutAsync(request, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Failure("Invalid credit card number format. Expected 16 digits."));

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Checkout_ShouldReturnOk_WhenPaymentSucceeds()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 15,
            CardHolderName = "Jane Doe",
            CardNumber = "4532015000000000",
            ExpiryMonth = "10",
            ExpiryYear = "2029",
            Cvv = "999"
        };

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = 15,
            TransactionReference = "TXN_TEST_123456",
            AmountPaid = 400,
            DoctorName = "Dr. Samir Cardiology",
            PatientName = "Jane Doe",
            PaidAt = DateTime.UtcNow
        };

        _paymentServiceMock.Setup(s => s.ProcessCheckoutAsync(request, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Success(receipt));

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);

        // Act
        var result = await _controller.GetReceipt(10);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnBadRequest_WhenReceiptNotFoundOrUnpaid()
    {
        // Arrange
        SetUserContext("patient_user_123");

        _paymentServiceMock.Setup(s => s.GetReceiptAsync(99, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Failure("No receipt available. Payment has not been completed."));

        // Act
        var result = await _controller.GetReceipt(99);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnOk_WhenReceiptFound()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var receipt = new PaymentReceiptDto
        {
            AppointmentId = 25,
            TransactionReference = "TXN_TEST_987654",
            AmountPaid = 300,
            DoctorName = "Dr. Mahmoud",
            PatientName = "Jane Doe",
            PaidAt = DateTime.UtcNow
        };

        _paymentServiceMock.Setup(s => s.GetReceiptAsync(25, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Success(receipt));

        // Act
        var result = await _controller.GetReceipt(25);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
