using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PaymobPaymentGatewayTests
{
    private readonly Mock<ILogger<PaymobPaymentGateway>> _loggerMock = new();

    private sealed class MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return sendFunc(request, cancellationToken);
        }
    }

    [Fact]
    public void ValidateWebhookHmac_WithValidHmac_ReturnsTrue()
    {
        // Arrange
        var hmacSecret = "test_hmac_secret_key_123456";
        var settings = Options.Create(new PaymobSettings
        {
            ApiKey = "test_api_key",
            HmacSecret = hmacSecret,
            IntegrationId = 12345,
            IframeId = "67890"
        });

        using var httpClient = new HttpClient();
        var gateway = new PaymobPaymentGateway(httpClient, settings, _loggerMock.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 9876543,
            Pending = false,
            Success = true,
            AmountCents = 35000,
            Currency = "EGP",
            CreatedAt = "2026-10-13T12:00:00.000000",
            ErrorOccured = false,
            HasParentTransaction = false,
            IntegrationId = 12345,
            Is3dSecure = true,
            IsAuth = false,
            IsCapture = false,
            IsRefunded = false,
            IsStandalonePayment = true,
            IsVoided = false,
            OrderId = 112233,
            Owner = "123",
            SourcePan = "2345",
            SourceSubType = "MasterCard",
            SourceType = "card"
        };

        // Construct official concatenation string
        var rawConcatenated =
            $"{callback.AmountCents}{callback.CreatedAt}{callback.Currency}falsefalse" +
            $"{callback.Id}{callback.IntegrationId}truefalsefalsefalse" +
            $"truefalse{callback.OrderId}{callback.Owner}false" +
            $"{callback.SourcePan}{callback.SourceSubType}{callback.SourceType}true";

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var expectedHmac = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawConcatenated))).ToLowerInvariant();

        // Act
        var isValid = gateway.ValidateWebhookHmac(callback, expectedHmac);

        // Assert
        isValid.Should().BeTrue("Authentic HMAC signature matching Paymob official specification must validate successfully");
    }

    [Fact]
    public void ValidateWebhookHmac_WithTamperedHmac_ReturnsFalse()
    {
        // Arrange
        var settings = Options.Create(new PaymobSettings
        {
            ApiKey = "test_api_key",
            HmacSecret = "test_hmac_secret",
            IntegrationId = 12345,
            IframeId = "67890"
        });

        using var httpClient = new HttpClient();
        var gateway = new PaymobPaymentGateway(httpClient, settings, _loggerMock.Object);

        var callback = new PaymobTransactionCallbackDto
        {
            Id = 1111,
            AmountCents = 50000,
            Success = true,
            Currency = "EGP"
        };

        // Act
        var isValid = gateway.ValidateWebhookHmac(callback, "tampered_or_invalid_hmac_signature_hex_value");

        // Assert
        isValid.Should().BeFalse("Tampered or incorrect HMAC signatures must be rejected");
    }

    [Fact]
    public async Task InitiatePaymentAsync_WhenUnconfigured_FailsGracefullyWithoutThrowing()
    {
        // Arrange
        var unconfiguredSettings = Options.Create(new PaymobSettings
        {
            ApiKey = null, // Missing
            HmacSecret = null,
            IntegrationId = 0
        });

        using var httpClient = new HttpClient();
        var gateway = new PaymobPaymentGateway(httpClient, unconfiguredSettings, _loggerMock.Object);

        var request = new PaymentInitiationRequestDto
        {
            AppointmentId = 10,
            Amount = 450m,
            Currency = "EGP",
            CustomerEmail = "test@patient.com",
            CustomerFullName = "Ahmed Hassan",
            TransactionReference = "TXN-001"
        };

        // Act
        var result = await gateway.InitiatePaymentAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("unconfigured");
    }

    [Fact]
    public async Task InitiatePaymentAsync_WhenApiCallSucceeds_Executes3StepFlowAndReturnsHostedCheckoutUrl()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            var uri = req.RequestUri!.PathAndQuery;
            if (uri.Contains("/auth/tokens"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"mock_auth_token_xyz\"}", Encoding.UTF8, "application/json")
                });
            }
            if (uri.Contains("/ecommerce/orders"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":998877}", Encoding.UTF8, "application/json")
                });
            }
            if (uri.Contains("/acceptance/payment_keys"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"mock_payment_token_abc\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new PaymobSettings
        {
            ApiKey = "valid_key",
            HmacSecret = "valid_secret",
            IntegrationId = 4455,
            IframeId = "889900",
            BaseUrl = "https://accept.paymob.com/api"
        });

        var gateway = new PaymobPaymentGateway(httpClient, settings, _loggerMock.Object);

        var request = new PaymentInitiationRequestDto
        {
            AppointmentId = 42,
            Amount = 300m,
            Currency = "EGP",
            CustomerEmail = "patient@clinic.com",
            CustomerFullName = "Karim Mahmoud",
            MerchantOrderId = "ORDER-APP42",
            TransactionReference = "TXN-APP42-123"
        };

        // Act
        var result = await gateway.InitiatePaymentAsync(request);

        // Assert
        handler.CallCount.Should().Be(3, "Paymob initiation requires Auth Token -> Order Registration -> Payment Key requests");
        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentToken.Should().Be("mock_payment_token_abc");
        result.Value.CheckoutUrl.Should().Contain("889900");
        result.Value.CheckoutUrl.Should().Contain("mock_payment_token_abc");
    }

    [Fact]
    public async Task InitiatePaymentAsync_WhenGatewayReturns500_ReturnsGracefulFailure()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new PaymobSettings
        {
            ApiKey = "valid_key",
            HmacSecret = "valid_secret",
            IntegrationId = 4455,
            IframeId = "889900"
        });

        var gateway = new PaymobPaymentGateway(httpClient, settings, _loggerMock.Object);

        var request = new PaymentInitiationRequestDto
        {
            AppointmentId = 42,
            Amount = 300m,
            Currency = "EGP"
        };

        // Act
        var result = await gateway.InitiatePaymentAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("authenticate");
    }
}
