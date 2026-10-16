namespace MediCare.Data.Entities;

public class PaymentAttempt : BaseAuditableEntity
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public long AmountCents { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = "Pending";
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public virtual Appointment Appointment { get; set; } = null!;
}
