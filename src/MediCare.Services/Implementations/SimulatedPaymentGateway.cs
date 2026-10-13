using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

/// <summary>
/// Development-only simulated payment gateway provider for local offline demonstrations.
/// Gated strictly by environment settings; rejected in production environments.
/// </summary>
public class SimulatedPaymentGateway : IPaymentGateway
{
    private readonly ILogger<SimulatedPaymentGateway> _logger;

    public string GatewayName => "Simulated";

    public SimulatedPaymentGateway(ILogger<SimulatedPaymentGateway> logger)
    {
        _logger = logger;
    }

    public Task<Result<PaymentInitiationResultDto>> InitiatePaymentAsync(
        PaymentInitiationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return Task.FromResult(Result<PaymentInitiationResultDto>.Failure("Request cannot be null."));
        }

        if (request.Amount <= 0)
        {
            return Task.FromResult(Result<PaymentInitiationResultDto>.Failure("Amount must be greater than zero."));
        }

        var token = $"sim_tok_{Guid.NewGuid():N}";
        var checkoutUrl = $"/Payment/SimulatedCheckout/{request.AppointmentId}?token={token}&ref={request.TransactionReference}";

        _logger.LogInformation("Simulated payment session initiated for appointment {ApptId}. Mock reference: {Ref}",
            request.AppointmentId, request.TransactionReference);

        return Task.FromResult(Result<PaymentInitiationResultDto>.Success(new PaymentInitiationResultDto
        {
            Success = true,
            PaymentToken = token,
            CheckoutUrl = checkoutUrl,
            TransactionReference = request.TransactionReference
        }));
    }

    public bool ValidateWebhookHmac(PaymobTransactionCallbackDto callback, string receivedHmac)
    {
        // In simulated mode, require non-empty HMAC string to ensure callers still follow security contract
        return !string.IsNullOrWhiteSpace(receivedHmac) && receivedHmac.StartsWith("sim_hmac_", StringComparison.OrdinalIgnoreCase);
    }

    public bool ValidateWebhookHmac(IDictionary<string, string> values, string receivedHmac)
    {
        return !string.IsNullOrWhiteSpace(receivedHmac) && receivedHmac.StartsWith("sim_hmac_", StringComparison.OrdinalIgnoreCase);
    }

    public Task<Result<PaymentVerificationResultDto>> VerifyPaymentAsync(
        string transactionReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
        {
            return Task.FromResult(Result<PaymentVerificationResultDto>.Failure("Transaction reference required."));
        }

        return Task.FromResult(Result<PaymentVerificationResultDto>.Success(new PaymentVerificationResultDto
        {
            Success = true,
            IsPaid = true,
            TransactionReference = transactionReference,
            AmountPaid = 100m,
            Currency = "EGP"
        }));
    }
}
