using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymentService
{
    Task<Result<PaymentReceiptDto>> ProcessCheckoutAsync(PaymentCheckoutRequestDto request, string currentUserId);
    Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount);
}
