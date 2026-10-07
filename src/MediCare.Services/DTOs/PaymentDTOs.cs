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
