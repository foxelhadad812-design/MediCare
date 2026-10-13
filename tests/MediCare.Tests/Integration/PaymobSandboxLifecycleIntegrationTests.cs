using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Integration;

/// <summary>
/// Comprehensive end-to-end integration test verifying the complete Paymob Egypt sandbox lifecycle:
/// 1. Session initiation (Token acquisition, Order registration, Payment Key request)
/// 2. Hosted iframe checkout URL validation
/// 3. Gateway sandbox transaction execution
/// 4. Authoritative server-to-server HMAC-SHA512 webhook reception
/// 5. Atomic appointment state transition (Pending -> Paid)
/// 6. Digital receipt retrieval and idempotent duplicate webhook protection.
/// </summary>
public class PaymobSandboxLifecycleIntegrationTests
{
    private sealed class MockPaymobHttpHandler : HttpMessageHandler
    {
        public int AuthTokenCalls { get; private set; }
        public int OrderCalls { get; private set; }
        public int PaymentKeyCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;

            if (uri.Contains("/auth/tokens"))
            {
                AuthTokenCalls++;
                var responseJson = JsonSerializer.Serialize(new { token = "sandbox_auth_token_xyz_123" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            }

            if (uri.Contains("/ecommerce/orders"))
            {
                OrderCalls++;
                var responseJson = JsonSerializer.Serialize(new { id = 987654321L });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            }

            if (uri.Contains("/acceptance/payment_keys"))
            {
                PaymentKeyCalls++;
                var responseJson = JsonSerializer.Serialize(new { token = "sandbox_payment_token_abc_789" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task FullLifecycle_PaymobSandboxPaymentJourney_SucceedsEndToEnd()
    {
        // -------------------------------------------------------------------------
        // ARRANGE: In-memory unit-of-work with clinical appointments and patients
        // -------------------------------------------------------------------------
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PaymobSandboxDb_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new ApplicationDbContext(options);

        var doctorUser = new ApplicationUser { Id = "doc_user_1", FullName = "Dr. Tarek Cardiology", Email = "doctor@clinic.com" };
        var patientUser = new ApplicationUser { Id = "pat_user_1", FullName = "Mona Ahmed", Email = "mona@patient.local", PhoneNumber = "+201012345678" };
        var doctor = new Doctor { Id = 1, UserId = "doc_user_1", User = doctorUser, Specialization = new Specialization { Id = 1, Name = "Cardiology" } };
        var patient = new Patient { Id = 1, UserId = "pat_user_1", User = patientUser };

        var appointment = new Appointment
        {
            Id = 1001,
            DoctorId = 1,
            Doctor = doctor,
            PatientId = 1,
            Patient = patient,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Unpaid,
            ConsultationFee = 500m
        };

        dbContext.Users.AddRange(doctorUser, patientUser);
        dbContext.Doctors.Add(doctor);
        dbContext.Patients.Add(patient);
        dbContext.Appointments.Add(appointment);
        await dbContext.SaveChangesAsync();

        var uow = new UnitOfWork(dbContext);
        var mockNotificationService = new Mock<INotificationService>();
        var mockLogger = NullLogger<PaymentService>.Instance;
        var mockGatewayLogger = NullLogger<PaymobPaymentGateway>.Instance;

        var paymobSettings = new PaymobSettings
        {
            Provider = "Paymob",
            ApiKey = "sandbox_secret_api_key_sample",
            HmacSecret = "SANDBOX_HMAC_SECRET_512_TEST_KEY_ABCDEF123456",
            IntegrationId = 8844,
            IframeId = "772211",
            BaseUrl = "https://accept.paymob.com/api",
            Currency = "EGP"
        };
        var optionsWrapper = Options.Create(paymobSettings);

        var httpHandler = new MockPaymobHttpHandler();
        using var httpClient = new HttpClient(httpHandler);
        var gateway = new PaymobPaymentGateway(httpClient, optionsWrapper, mockGatewayLogger);

        var paymentService = new PaymentService(uow, mockNotificationService.Object, mockLogger, gateway, optionsWrapper);

        // -------------------------------------------------------------------------
        // STEP 1: Initiation of checkout session via IPaymentService
        // -------------------------------------------------------------------------
        var initResult = await paymentService.InitiateAppointmentPaymentAsync(1001, null, "pat_user_1");

        initResult.IsSuccess.Should().BeTrue("Payment initiation should succeed with configured Paymob credentials");
        initResult.Value.Should().NotBeNull();
        initResult.Value!.Amount.Should().Be(500m);
        initResult.Value.Currency.Should().Be("EGP");
        initResult.Value.CheckoutUrl.Should().StartWith("https://accept.paymob.com/api/acceptance/iframes/772211?payment_token=sandbox_payment_token_abc_789");

        // Verify gateway network interactions: Auth Token -> Order Registration -> Payment Key Request
        httpHandler.AuthTokenCalls.Should().Be(1);
        httpHandler.OrderCalls.Should().Be(1);
        httpHandler.PaymentKeyCalls.Should().Be(1);

        // Verify database state: status transitioned to Pending
        var pendingAppt = await uow.Appointments.GetByIdAsync(1001);
        pendingAppt!.PaymentStatus.Should().Be(PaymentStatus.Pending);
        pendingAppt.TransactionReference.Should().NotBeNullOrEmpty();

        // -------------------------------------------------------------------------
        // STEP 2: Client hosted redirection verification
        // -------------------------------------------------------------------------
        Uri.TryCreate(initResult.Value.CheckoutUrl, UriKind.Absolute, out var parsedCheckoutUri).Should().BeTrue();
        parsedCheckoutUri!.Host.Should().Be("accept.paymob.com");
        parsedCheckoutUri.Scheme.Should().Be("https");

        // -------------------------------------------------------------------------
        // STEP 3 & 4: Paymob server-to-server webhook callback with cryptographic HMAC
        // -------------------------------------------------------------------------
        var callbackPayload = new PaymobTransactionCallbackDto
        {
            Id = 5544332211L,
            Pending = false,
            Success = true,
            AmountCents = 50000, // 500.00 EGP in cents
            Currency = "EGP",
            CreatedAt = "2026-10-13T19:00:00.000000",
            ErrorOccured = false,
            HasParentTransaction = false,
            IntegrationId = 8844,
            Is3dSecure = true,
            IsAuth = false,
            IsCapture = true,
            IsRefunded = false,
            IsStandalonePayment = true,
            IsVoided = false,
            OrderId = 987654321L,
            MerchantOrderId = $"ORDER-APP1001-{DateTime.UtcNow.Ticks}",
            Owner = "12345",
            SourcePan = "2346",
            SourceSubType = "MasterCard",
            SourceType = "card"
        };

        // Compute authoritative HMAC-SHA512 signature using gateway validator helper
        var hmacInput = new StringBuilder();
        hmacInput.Append(callbackPayload.AmountCents);
        hmacInput.Append(callbackPayload.CreatedAt);
        hmacInput.Append(callbackPayload.Currency);
        hmacInput.Append("false"); // error_occured
        hmacInput.Append("false"); // has_parent_transaction
        hmacInput.Append(callbackPayload.Id);
        hmacInput.Append(callbackPayload.IntegrationId);
        hmacInput.Append("true");  // is_3d_secure
        hmacInput.Append("false"); // is_auth
        hmacInput.Append("true");  // is_capture
        hmacInput.Append("false"); // is_refunded
        hmacInput.Append("true");  // is_standalone_payment
        hmacInput.Append("false"); // is_voided
        hmacInput.Append(callbackPayload.OrderId);
        hmacInput.Append(callbackPayload.Owner);
        hmacInput.Append("false"); // pending
        hmacInput.Append(callbackPayload.SourcePan);
        hmacInput.Append(callbackPayload.SourceSubType);
        hmacInput.Append(callbackPayload.SourceType);
        hmacInput.Append("true");  // success

        using var hmacSha512 = new System.Security.Cryptography.HMACSHA512(Encoding.UTF8.GetBytes(paymobSettings.HmacSecret));
        var computedHmac = Convert.ToHexString(hmacSha512.ComputeHash(Encoding.UTF8.GetBytes(hmacInput.ToString()))).ToLowerInvariant();

        callbackPayload.Hmac = computedHmac;

        // Process webhook
        var webhookResult = await paymentService.ProcessWebhookCallbackAsync(callbackPayload, computedHmac);

        webhookResult.IsSuccess.Should().BeTrue("Authoritative webhook must succeed with valid HMAC and matching amount/currency");
        webhookResult.Value.Should().NotBeNull();
        webhookResult.Value!.AmountPaid.Should().Be(500m);
        webhookResult.Value.TransactionReference.Should().Be("PAYMOB-5544332211");

        // -------------------------------------------------------------------------
        // STEP 5: Verification of persistent database record
        // -------------------------------------------------------------------------
        var finalAppt = await uow.Appointments.GetByIdAsync(1001);
        finalAppt!.PaymentStatus.Should().Be(PaymentStatus.Paid);
        finalAppt.PaidAmount.Should().Be(500m);
        finalAppt.TransactionReference.Should().Be("PAYMOB-5544332211");
        finalAppt.PaidAt.Should().NotBeNull();

        // -------------------------------------------------------------------------
        // STEP 6: Digital receipt retrieval
        // -------------------------------------------------------------------------
        var receiptResult = await paymentService.GetReceiptAsync(1001, "pat_user_1");
        receiptResult.IsSuccess.Should().BeTrue();
        receiptResult.Value!.PatientName.Should().Be("Mona Ahmed");
        receiptResult.Value.DoctorName.Should().Contain("Tarek Cardiology");
        receiptResult.Value.Specialization.Should().Be("Cardiology");
        receiptResult.Value.PaymentMethod.Should().Be("Paymob Egypt (Online Card)");

        // -------------------------------------------------------------------------
        // STEP 7: Duplicate webhook idempotency verification
        // -------------------------------------------------------------------------
        var dupResult = await paymentService.ProcessWebhookCallbackAsync(callbackPayload, computedHmac);
        dupResult.IsSuccess.Should().BeTrue("Duplicate callback must be handled idempotently");
        dupResult.Value!.TransactionReference.Should().Be("PAYMOB-5544332211");

        // Appointment remains Paid with zero state mutation
        finalAppt.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Theory]
    [InlineData("Stripe")]
    [InlineData("Paymoob")]
    [InlineData("UnknownProvider")]
    [InlineData("")]
    public void UnknownPaymentProvider_HaltsExecution_WithInvalidOperationException(string unknownProvider)
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new PaymobSettings { Provider = unknownProvider };
        services.AddSingleton(Options.Create(settings));
        services.AddLogging();
        services.AddHttpClient();

        MediCare.Services.Extensions.ServiceCollectionExtensions.AddApplicationServices(services);

        using var serviceProvider = services.BuildServiceProvider();

        // Act & Assert: Resolving IPaymentGateway with an unknown provider MUST throw InvalidOperationException
        var action = () => serviceProvider.GetRequiredService<IPaymentGateway>();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Unsupported payment provider '{unknownProvider}'*");
    }
}
