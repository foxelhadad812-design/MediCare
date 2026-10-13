using System.Threading;
using System.Threading.Tasks;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymentService
{
    Task<Result<PaymentReceiptDto>> ProcessCheckoutAsync(PaymentCheckoutRequestDto request, string currentUserId);
    Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount);

    /// <summary>
    /// Initiates a gateway payment session for an appointment with authoritative server amount calculation.
    /// </summary>
    Task<Result<PaymentInitiateResultDto>> InitiateAppointmentPaymentAsync(
        int appointmentId,
        string? promoCode,
        string currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cryptographically validates and idempotently processes a Paymob webhook transaction callback.
    /// </summary>
    Task<Result<PaymentReceiptDto>> ProcessWebhookCallbackAsync(
        PaymobTransactionCallbackDto callback,
        string receivedHmac,
        CancellationToken cancellationToken = default);
}

