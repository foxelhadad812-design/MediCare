namespace MediCare.Services.DTOs;

public class PaymentCheckoutRequestDto
{
    public int AppointmentId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string CardHolderName { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string Cvv { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "CreditCard";
    public string? PromoCode { get; set; }
}

public class PaymentReceiptDto
{
    public int AppointmentId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? AppliedPromoCode { get; set; }
    public DateTime PaidAt { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string FormattedDate { get; set; } = string.Empty;
    public string FormattedTime { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Online Card Payment";
}

public class PromoCodeValidationDto
{
    public bool IsValid { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PaymentInitiateRequestDto
{
    public int AppointmentId { get; set; }
    public string? PromoCode { get; set; }
}

public class PaymentInitiateResultDto
{
    public bool Success { get; set; }
    public string CheckoutUrl { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? ErrorMessage { get; set; }
}

public class PaymentInitiationRequestDto
{
    public int AppointmentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerFullName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string MerchantOrderId { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
}

public class PaymentInitiationResultDto
{
    public bool Success { get; set; }
    public string PaymentToken { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string TransactionReference { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class PaymentVerificationResultDto
{
    public bool Success { get; set; }
    public bool IsPaid { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Paymob transaction processed callback model representing standard transaction notification fields.
/// </summary>
public class PaymobTransactionCallbackDto
{
    public long Id { get; set; }
    public bool Pending { get; set; }
    public bool Success { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "EGP";
    public string CreatedAt { get; set; } = string.Empty;
    public bool ErrorOccured { get; set; }
    public bool HasParentTransaction { get; set; }
    public int IntegrationId { get; set; }
    public bool Is3dSecure { get; set; }
    public bool IsAuth { get; set; }
    public bool IsCapture { get; set; }
    public bool IsRefunded { get; set; }
    public bool IsStandalonePayment { get; set; }
    public bool IsVoided { get; set; }
    public long OrderId { get; set; }
    public string? MerchantOrderId { get; set; }
    public string? Owner { get; set; }
    public string? SourcePan { get; set; }
    public string? SourceSubType { get; set; }
    public string? SourceType { get; set; }
    public string? Hmac { get; set; }
}

