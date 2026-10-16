using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MediCare.Tests.Services;

public class PaymobCheckoutServiceTests
{
    private const string HmacSecret = "test-hmac-secret";
    private const string OrderedValues = "25002026-10-16T12:30:00ZEGPfalsefalse123456789123456truefalsefalsefalsetruefalse4567892346false2346MasterCardcardtrue";

    private const string TransactionJson = """
        {"amount_cents":2500,"created_at":"2026-10-16T12:30:00Z","currency":"EGP","error_occured":false,"has_parent_transaction":false,"id":123456789,"integration_id":123456,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":456789},"owner":2346,"pending":false,"source_data":{"pan":"2346","sub_type":"MasterCard","type":"card"},"success":true}
        """;

    [Fact]
    public void VerifyTransactionHmac_AcceptsValidPaymobSignature()
    {
        using var document = JsonDocument.Parse(TransactionJson);
        var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(HmacSecret), Encoding.UTF8.GetBytes(OrderedValues))).ToLowerInvariant();

        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyTransactionHmac_RejectsTamperedOrMissingSignature()
    {
        using var document = JsonDocument.Parse(TransactionJson);

        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, "invalid").Should().BeFalse();
        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, null).Should().BeFalse();
    }

    [Fact]
    public void CheckoutRequest_DoesNotBindCardholderData()
    {
        var request = JsonSerializer.Deserialize<PaymentCheckoutRequestDto>("""
            {"appointmentId":7,"cardNumber":"4111111111111111","cardHolderName":"Test","expiryMonth":"12","expiryYear":"2030","cvv":"123","promoCode":"WELCOME10"}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request.Should().NotBeNull();
        request!.AppointmentId.Should().Be(7);
        request.PromoCode.Should().Be("WELCOME10");
        request.CardNumber.Should().BeEmpty();
        request.Cvv.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCheckoutAsync_RejectsAnotherPatientsAppointment_WithoutAttemptOrProviderRequest()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        var owner = new ApplicationUser
        {
            Id = "patient-owner",
            FullName = "Appointment Owner",
            Email = "owner@example.test",
            PhoneNumber = "+201000000001"
        };
        var patient = new Patient { UserId = owner.Id, User = owner, Gender = "Not specified" };
        var doctorUser = new ApplicationUser { Id = "doctor-user", FullName = "Test Doctor" };
        var specialization = new Specialization { Name = "General Practice" };
        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            User = doctorUser,
            Specialization = specialization,
            LicenseNumber = "TEST-ONLY",
            ConsultationFee = 500m
        };
        var appointment = new Appointment
        {
            Patient = patient,
            Doctor = doctor,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(9, 30, 0),
            Status = AppointmentStatus.Confirmed,
            ConsultationFee = 500m,
            PaymentStatus = PaymentStatus.Unpaid
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var providerRequestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            providerRequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"client_secret":"test-client-secret","intention_order_id":98765}""")
            });
        }));
        var paymentService = new Mock<IPaymentService>();
        var service = new PaymobCheckoutService(
            db,
            httpClient,
            Options.Create(new PaymobSettings
            {
                SecretKey = "sk_test_unit_test",
                PublicKey = "egy_pk_test_unit_test",
                HmacSecret = "unit-test-hmac",
                IntegrationId = "123456",
                PublicBaseUrl = "https://demo.example.test/"
            }),
            paymentService.Object,
            NullLogger<PaymobCheckoutService>.Instance);

        var unauthorized = await service.CreateCheckoutAsync(appointment.Id, "different-patient", null);

        unauthorized.IsSuccess.Should().BeFalse();
        unauthorized.Error.Should().Contain("Forbidden");
        (await db.PaymentAttempts.CountAsync()).Should().Be(0);
        providerRequestCount.Should().Be(0, "unauthorized ownership must be checked before any provider request");

        var ownerResult = await service.CreateCheckoutAsync(appointment.Id, owner.Id, null);

        ownerResult.IsSuccess.Should().BeTrue("the appointment owner should still be able to start hosted checkout");
        ownerResult.Value!.CheckoutUrl.Should().StartWith("https://accept.paymob.com/unifiedcheckout/");
        (await db.PaymentAttempts.CountAsync()).Should().Be(1);
        providerRequestCount.Should().Be(1, "the provider HTTP call is intercepted by a test-only handler");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
