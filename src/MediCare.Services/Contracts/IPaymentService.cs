using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymentService
{
    Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount, string? currentUserId);
}
