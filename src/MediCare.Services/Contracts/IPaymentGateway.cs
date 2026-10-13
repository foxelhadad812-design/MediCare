using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

/// <summary>
/// Pluggable payment gateway abstraction decoupling provider-specific operations
/// (such as Paymob Egypt) from core clinic business logic.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Distinct identifier of the payment gateway implementation (e.g., "Paymob", "Simulated").
    /// </summary>
    string GatewayName { get; }

    /// <summary>
    /// Initiates a hosted or tokenized payment session with the gateway provider.
    /// </summary>
    Task<Result<PaymentInitiationResultDto>> InitiatePaymentAsync(
        PaymentInitiationRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cryptographically verifies the authenticity of a webhook/callback notification using provider HMAC.
    /// </summary>
    bool ValidateWebhookHmac(PaymobTransactionCallbackDto callback, string receivedHmac);

    /// <summary>
    /// Cryptographically verifies the authenticity of a query string/dictionary callback using provider HMAC.
    /// </summary>
    bool ValidateWebhookHmac(IDictionary<string, string> values, string receivedHmac);

    /// <summary>
    /// Performs an authoritative server-to-server transaction status inquiry.
    /// </summary>
    Task<Result<PaymentVerificationResultDto>> VerifyPaymentAsync(
        string transactionReference,
        CancellationToken cancellationToken = default);
}
