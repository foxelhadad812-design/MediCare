using System.Text.RegularExpressions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IUnitOfWork uow,
        INotificationService notificationService,
        ILogger<PaymentService> logger)
    {
        _uow = uow;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<PaymentReceiptDto>> ProcessCheckoutAsync(PaymentCheckoutRequestDto request, string currentUserId)
    {
        if (request == null)
        {
            return Result<PaymentReceiptDto>.Failure("Payment request cannot be empty.");
        }

        if (request.AppointmentId <= 0)
        {
            return Result<PaymentReceiptDto>.Failure("Valid appointment ID is required.");
        }

        // Card validation
        var cleanCard = Regex.Replace(request.CardNumber ?? string.Empty, @"\s+|-", "");
        if (string.IsNullOrWhiteSpace(cleanCard) || !Regex.IsMatch(cleanCard, @"^\d{13,19}$"))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid card number. Please provide a valid 13-19 digit credit or debit card number.");
        }

        if (!IsValidLuhn(cleanCard))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid card checksum. Please check your card number.");
        }

        if (string.IsNullOrWhiteSpace(request.CardHolderName) || request.CardHolderName.Trim().Length < 2)
        {
            return Result<PaymentReceiptDto>.Failure("Cardholder name is required.");
        }

        if (!int.TryParse(request.ExpiryMonth, out int expMonth) || expMonth < 1 || expMonth > 12)
        {
            return Result<PaymentReceiptDto>.Failure("Invalid expiration month.");
        }

        if (!int.TryParse(request.ExpiryYear, out int expYear))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid expiration year.");
        }

        if (expYear < 100)
        {
            expYear += 2000;
        }

        var now = DateTime.UtcNow;
        if (expYear < now.Year || (expYear == now.Year && expMonth < now.Month))
        {
            return Result<PaymentReceiptDto>.Failure("The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(request.Cvv) || !Regex.IsMatch(request.Cvv.Trim(), @"^\d{3,4}$"))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid CVV. Please enter a valid 3 or 4 digit security code.");
        }

        // Fetch appointment
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(request.AppointmentId);
        if (appointment == null)
        {
            return Result<PaymentReceiptDto>.Failure("Appointment not found.");
        }

        // IDOR check: Only the patient who booked the appointment can process payment
        if (appointment.Patient?.UserId != currentUserId)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted payment for appointment {ApptId} owned by patient user {PatientUserId}",
                currentUserId, request.AppointmentId, appointment.Patient?.UserId);
            return Result<PaymentReceiptDto>.Failure("Forbidden: You are not authorized to pay for this appointment.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Rejected)
        {
            return Result<PaymentReceiptDto>.Failure("Cannot process payment for a cancelled or rejected appointment.");
        }

        if (appointment.PaymentStatus == PaymentStatus.Paid)
        {
            return Result<PaymentReceiptDto>.Failure("Appointment consultation fee has already been paid.");
        }

        // Atomically mark paid
        decimal originalFee = appointment.ConsultationFee;
        decimal discountAmount = 0m;
        string? appliedPromo = null;

        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var promoRes = await ValidatePromoCodeAsync(request.PromoCode, originalFee);
            if (promoRes.IsSuccess && promoRes.Value != null && promoRes.Value.IsValid)
            {
                discountAmount = promoRes.Value.DiscountAmount;
                appliedPromo = promoRes.Value.Code;
            }
        }

        decimal finalAmount = Math.Max(0m, originalFee - discountAmount);

        appointment.PaymentStatus = PaymentStatus.Paid;
        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        var txnRef = $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{appointment.Id:D4}";

        try
        {
            var promoNotice = !string.IsNullOrEmpty(appliedPromo) ? $" (Promo {appliedPromo} applied: -${discountAmount:F2})" : "";
            await _notificationService.SendNotificationAsync(
                currentUserId,
                "Payment Received",
                $"Payment of ${finalAmount:F2} for appointment #{appointment.Id} was completed successfully{promoNotice}. Ref: {txnRef}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for payment {TxnRef}", txnRef);
        }

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = finalAmount,
            OriginalAmount = originalFee,
            DiscountAmount = discountAmount,
            AppliedPromoCode = appliedPromo,
            PaidAt = DateTime.UtcNow,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = "Online Card Payment"
        };

        return Result<PaymentReceiptDto>.Success(receipt);
    }

    public async Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId)
    {
        if (appointmentId <= 0)
        {
            return Result<PaymentReceiptDto>.Failure("Valid appointment ID is required.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result<PaymentReceiptDto>.Failure("Appointment not found.");
        }

        // Access check: appointment patient, doctor, or authorized
        bool isPatient = appointment.Patient?.UserId == currentUserId;
        bool isDoctor = appointment.Doctor?.UserId == currentUserId;

        if (!isPatient && !isDoctor)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to receipt for appointment {ApptId}",
                currentUserId, appointmentId);
            return Result<PaymentReceiptDto>.Failure("Forbidden: You are not authorized to access this receipt.");
        }

        if (appointment.PaymentStatus != PaymentStatus.Paid)
        {
            return Result<PaymentReceiptDto>.Failure("Payment has not been completed for this appointment yet.");
        }

        var paidDate = appointment.UpdatedAt ?? appointment.CreatedAt;
        var txnRef = $"TXN-{paidDate:yyyyMMddHHmmss}-{appointment.Id:D4}";

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = appointment.ConsultationFee,
            PaidAt = paidDate,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = "Online Card Payment"
        };

        return Result<PaymentReceiptDto>.Success(receipt);
    }

    private static bool IsValidLuhn(string number)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = number.Length - 1; i >= 0; i--)
        {
            int n = number[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    public Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(Result<PromoCodeValidationDto>.Failure("Please enter a valid promo code."));
        }

        var normalized = code.Trim().ToUpperInvariant();
        decimal discount = 0m;
        string desc = string.Empty;

        switch (normalized)
        {
            case "DEPI2026":
                discount = Math.Round(currentAmount * 0.20m, 2);
                desc = "DEPI 2026 Scholarship Special: 20% Discount";
                break;
            case "MEDICARE50":
                discount = Math.Min(currentAmount, 50m);
                desc = "Medicare Voucher: 50 EGP Instant Discount";
                break;
            case "WELCOME10":
                discount = Math.Round(currentAmount * 0.10m, 2);
                desc = "New Patient Welcome: 10% Discount";
                break;
            default:
                return Task.FromResult(Result<PromoCodeValidationDto>.Failure("Invalid or expired promo code."));
        }

        var dto = new PromoCodeValidationDto
        {
            IsValid = true,
            Code = normalized,
            DiscountAmount = discount,
            FinalAmount = Math.Max(0m, currentAmount - discount),
            Description = desc
        };

        return Task.FromResult(Result<PromoCodeValidationDto>.Success(dto));
    }
}
