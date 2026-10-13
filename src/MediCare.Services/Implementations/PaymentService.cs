using System.Text.RegularExpressions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymobSettings _paymobSettings;

    public PaymentService(
        IUnitOfWork uow,
        INotificationService notificationService,
        ILogger<PaymentService> logger,
        IPaymentGateway? paymentGateway = null,
        IOptions<PaymobSettings>? paymobSettings = null)
    {
        _uow = uow;
        _notificationService = notificationService;
        _logger = logger;
        _paymentGateway = paymentGateway ?? new SimulatedPaymentGateway(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedPaymentGateway>.Instance);
        _paymobSettings = paymobSettings?.Value ?? new PaymobSettings();
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

        if (string.Equals(_paymobSettings.Provider, "Paymob", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Payment checkout simulation rejected for user {UserId}: Paymob gateway is the active provider.", currentUserId);
            return Result<PaymentReceiptDto>.Failure("Direct card checkout simulation is disabled when Paymob payment gateway is active. Please initiate checkout via Paymob hosted payment gateway.");
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
            _logger.LogInformation("Appointment {ApptId} has already been paid. Returning existing receipt (idempotent request).", appointment.Id);
            return await GetReceiptAsync(appointment.Id, currentUserId);
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
        var paymentTimeUtc = DateTime.UtcNow;
        var txnRef = $"TXN-{paymentTimeUtc:yyyyMMddHHmmss}-{appointment.Id:D4}";

        appointment.PaymentStatus = PaymentStatus.Paid;
        appointment.PaidAmount = finalAmount;
        appointment.DiscountAmount = discountAmount;
        appointment.PromoCode = appliedPromo;
        appointment.TransactionReference = txnRef;
        appointment.PaidAt = paymentTimeUtc;

        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during payment checkout for appointment {ApptId}. Another concurrent request already processed payment.", appointment.Id);
            return await GetReceiptAsync(appointment.Id, currentUserId);
        }

        try
        {
            var promoNotice = !string.IsNullOrEmpty(appliedPromo) ? $" (Promo {appliedPromo} applied: -{discountAmount:F2} EGP)" : "";
            await _notificationService.SendNotificationAsync(
                currentUserId,
                "Payment Received",
                $"Payment of {finalAmount:F2} EGP for appointment #{appointment.Id} was completed successfully{promoNotice}. Ref: {txnRef}");
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
            PaidAt = paymentTimeUtc,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = "Simulated Card Checkout"
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

        var paidDate = appointment.PaidAt ?? appointment.UpdatedAt ?? appointment.CreatedAt;
        var txnRef = appointment.TransactionReference ?? $"TXN-{paidDate:yyyyMMddHHmmss}-{appointment.Id:D4}";

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = appointment.PaidAmount ?? appointment.ConsultationFee,
            OriginalAmount = appointment.ConsultationFee,
            DiscountAmount = appointment.DiscountAmount ?? 0m,
            AppliedPromoCode = appointment.PromoCode,
            PaidAt = paidDate,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = (txnRef.StartsWith("PAYMOB-", StringComparison.OrdinalIgnoreCase))
                ? "Paymob Egypt (Online Card)"
                : "Simulated Card Checkout"
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

    public async Task<Result<PaymentInitiateResultDto>> InitiateAppointmentPaymentAsync(
        int appointmentId,
        string? promoCode,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId <= 0)
        {
            return Result<PaymentInitiateResultDto>.Failure("Valid appointment ID is required.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result<PaymentInitiateResultDto>.Failure("Appointment not found.");
        }

        // IDOR check: Only the patient who booked the appointment can initiate payment
        if (appointment.Patient?.UserId != currentUserId)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted payment initiation for appointment {ApptId} owned by patient user {PatientUserId}",
                currentUserId, appointmentId, appointment.Patient?.UserId);
            return Result<PaymentInitiateResultDto>.Failure("Forbidden: You are not authorized to pay for this appointment.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Rejected)
        {
            return Result<PaymentInitiateResultDto>.Failure("Cannot process payment for a cancelled or rejected appointment.");
        }

        if (appointment.PaymentStatus == PaymentStatus.Paid)
        {
            return Result<PaymentInitiateResultDto>.Failure("Appointment has already been paid.");
        }

        // Server-calculated authoritative fees
        decimal originalFee = appointment.ConsultationFee;
        decimal discountAmount = 0m;
        string? appliedPromo = null;

        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var promoRes = await ValidatePromoCodeAsync(promoCode, originalFee);
            if (promoRes.IsSuccess && promoRes.Value != null && promoRes.Value.IsValid)
            {
                discountAmount = promoRes.Value.DiscountAmount;
                appliedPromo = promoRes.Value.Code;
            }
        }

        decimal payableAmount = Math.Max(0m, originalFee - discountAmount);
        var paymentTimeUtc = DateTime.UtcNow;
        var txnRef = $"TXN-{paymentTimeUtc:yyyyMMddHHmmss}-{appointment.Id:D4}";

        var merchantOrderId = $"ORDER-APP{appointment.Id}-{paymentTimeUtc.Ticks}";

        var initiationRequest = new PaymentInitiationRequestDto
        {
            AppointmentId = appointment.Id,
            Amount = payableAmount,
            Currency = "EGP",
            CustomerEmail = appointment.Patient?.User?.Email ?? "patient@medicare.local",
            CustomerFullName = appointment.Patient?.User?.FullName ?? "Patient",
            CustomerPhone = appointment.Patient?.User?.PhoneNumber ?? "+201000000000",
            MerchantOrderId = merchantOrderId,
            TransactionReference = merchantOrderId
        };

        var gatewayResult = await _paymentGateway.InitiatePaymentAsync(initiationRequest, cancellationToken);
        if (!gatewayResult.IsSuccess || gatewayResult.Value == null)
        {
            return Result<PaymentInitiateResultDto>.Failure(gatewayResult.Error ?? "Payment gateway failed to initialize checkout session.");
        }

        // Validate Checkout URL to prevent open redirect vulnerabilities
        var checkoutUrl = gatewayResult.Value.CheckoutUrl;
        if (!string.IsNullOrWhiteSpace(checkoutUrl))
        {
            if (!Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var parsedUri) ||
                parsedUri.Scheme != Uri.UriSchemeHttps ||
                (!parsedUri.Host.EndsWith("paymob.com", StringComparison.OrdinalIgnoreCase) &&
                 !parsedUri.Host.Equals(new Uri(_paymobSettings.BaseUrl).Host, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Untrusted checkout URL received from gateway: {Url}", checkoutUrl);
                return Result<PaymentInitiateResultDto>.Failure("Untrusted checkout URL generated by payment gateway.");
            }
        }

        // Save pending payment metadata with authoritative initiation order reference
        appointment.PaymentStatus = PaymentStatus.Pending;
        appointment.DiscountAmount = discountAmount;
        appointment.PromoCode = appliedPromo;
        appointment.TransactionReference = merchantOrderId;

        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        return Result<PaymentInitiateResultDto>.Success(new PaymentInitiateResultDto
        {
            Success = true,
            CheckoutUrl = gatewayResult.Value.CheckoutUrl,
            TransactionReference = merchantOrderId,
            Amount = payableAmount,
            Currency = "EGP"
        });
    }

    public async Task<Result<PaymentReceiptDto>> ProcessWebhookCallbackAsync(
        PaymobTransactionCallbackDto callback,
        string receivedHmac,
        CancellationToken cancellationToken = default)
    {
        if (callback == null)
        {
            return Result<PaymentReceiptDto>.Failure("Callback payload cannot be null.");
        }

        // 1. Cryptographic HMAC validation
        if (!_paymentGateway.ValidateWebhookHmac(callback, receivedHmac))
        {
            _logger.LogWarning("Security Alert: Invalid Paymob webhook HMAC signature. Transaction {TxnId} rejected.", callback.Id);
            return Result<PaymentReceiptDto>.Failure("Invalid HMAC signature.");
        }

        // 2. Identify and validate target appointment by MerchantOrderId
        if (string.IsNullOrWhiteSpace(callback.MerchantOrderId) ||
            !callback.MerchantOrderId.StartsWith("ORDER-APP", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Paymob callback rejected: Missing or invalid MerchantOrderId format. Received: {OrderId}", callback.MerchantOrderId);
            return Result<PaymentReceiptDto>.Failure("Invalid merchant order ID in callback.");
        }

        int appointmentId = 0;
        var parts = callback.MerchantOrderId.Substring("ORDER-APP".Length).Split('-');
        if (parts.Length > 0) int.TryParse(parts[0], out appointmentId);

        if (appointmentId <= 0)
        {
            _logger.LogWarning("Paymob callback rejected: Unable to parse appointment ID from order: {OrderId}", callback.MerchantOrderId);
            return Result<PaymentReceiptDto>.Failure("Invalid appointment ID embedded in merchant order.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            _logger.LogWarning("Paymob callback received for unknown appointment {ApptId}. MerchantOrderId: {OrderId}", appointmentId, callback.MerchantOrderId);
            return Result<PaymentReceiptDto>.Failure("Associated appointment not found.");
        }

        // 3. Exact MerchantOrderId Session Matching:
        // Ensure the received callback correlates with the exact authoritative MerchantOrderId saved during initiation
        if (appointment.PaymentStatus == PaymentStatus.Pending &&
            !string.IsNullOrEmpty(appointment.TransactionReference) &&
            !string.Equals(appointment.TransactionReference, callback.MerchantOrderId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Security Alert: Paymob callback MerchantOrderId mismatch for appointment {ApptId}. Recorded: '{Expected}', Received: '{Received}'",
                appointment.Id, appointment.TransactionReference, callback.MerchantOrderId);
            return Result<PaymentReceiptDto>.Failure("Merchant order ID does not match active payment initiation session.");
        }

        // 4. Clinical Status Verification: Check that appointment is in an active, payable clinical state
        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Rejected)
        {
            _logger.LogWarning("Paymob callback rejected: Appointment {ApptId} has clinical status '{Status}'.", appointment.Id, appointment.Status);
            return Result<PaymentReceiptDto>.Failure("Cannot process payment for a cancelled or rejected appointment.");
        }

        // 5. Integration ID Verification: Ensure callback matches configured merchant integration
        if (_paymobSettings.IntegrationId > 0 && callback.IntegrationId != _paymobSettings.IntegrationId)
        {
            _logger.LogWarning("Security Alert: Paymob callback integration ID mismatch for appointment {ApptId}. Configured: {ConfiguredId}, Received: {ReceivedId}",
                appointment.Id, _paymobSettings.IntegrationId, callback.IntegrationId);
            return Result<PaymentReceiptDto>.Failure("Integration ID mismatch in webhook callback.");
        }

        // 4. Reject pending, refunded, voided, error, or non-success transactions
        bool isPayableSuccess = callback.Success && !callback.Pending && !callback.IsVoided && !callback.IsRefunded && !callback.ErrorOccured;
        if (!isPayableSuccess)
        {
            _logger.LogWarning("Paymob reported non-payable transaction for appointment {ApptId} (Success: {Success}, Pending: {Pending}, Voided: {Voided}, Refunded: {Refunded}, ErrorOccurred: {Error}). Txn: {TxnId}",
                appointment.Id, callback.Success, callback.Pending, callback.IsVoided, callback.IsRefunded, callback.ErrorOccured, callback.Id);

            // Stale or failed callback must NEVER demote an already paid appointment
            if (appointment.PaymentStatus == PaymentStatus.Paid)
            {
                _logger.LogInformation("Appointment {ApptId} is already paid. Preserving Paid status despite non-payable callback.", appointment.Id);
                var patientUserId = appointment.Patient?.UserId ?? string.Empty;
                return await GetReceiptAsync(appointment.Id, patientUserId);
            }

            appointment.PaymentStatus = PaymentStatus.Failed;
            _uow.Appointments.Update(appointment);
            await _uow.CommitAsync();

            return Result<PaymentReceiptDto>.Failure("Payment transaction is not an authorized, completed payment.");
        }

        // 5. Idempotency Check: if already marked Paid, return existing receipt without duplicate processing
        if (appointment.PaymentStatus == PaymentStatus.Paid)
        {
            _logger.LogInformation("Idempotent webhook: Appointment {ApptId} is already paid. Returning existing receipt.", appointment.Id);
            var patientUserId = appointment.Patient?.UserId ?? string.Empty;
            return await GetReceiptAsync(appointment.Id, patientUserId);
        }

        // 6. Authoritative amount & currency verification
        decimal originalFee = appointment.ConsultationFee;
        decimal discountAmount = appointment.DiscountAmount ?? 0m;
        decimal expectedPayable = Math.Max(0m, originalFee - discountAmount);
        long expectedCents = (long)Math.Round(expectedPayable * 100m);

        if (callback.AmountCents != expectedCents)
        {
            _logger.LogError("Payment amount mismatch detected for appointment {ApptId}! Expected: {Exp} cents, Received: {Rec} cents.",
                appointment.Id, expectedCents, callback.AmountCents);
            return Result<PaymentReceiptDto>.Failure("Payment amount does not match authoritative fee.");
        }

        if (!string.Equals(callback.Currency, "EGP", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Payment currency mismatch detected for appointment {ApptId}! Expected: EGP, Received: {Curr}",
                appointment.Id, callback.Currency);
            return Result<PaymentReceiptDto>.Failure("Invalid transaction currency.");
        }

        // 7. Atomically transition state to Paid
        var paidTime = DateTime.UtcNow;
        var txnRef = $"PAYMOB-{callback.Id}";

        appointment.PaymentStatus = PaymentStatus.Paid;
        appointment.PaidAmount = expectedPayable;
        appointment.TransactionReference = txnRef;
        appointment.PaidAt = paidTime;

        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during webhook callback for appointment {ApptId}.", appointment.Id);
            var reloaded = await _uow.Appointments.GetByIdWithDetailsAsync(appointment.Id);
            var patientUserId = reloaded?.Patient?.UserId ?? appointment.Patient?.UserId ?? string.Empty;
            if (reloaded?.PaymentStatus == PaymentStatus.Paid)
            {
                return await GetReceiptAsync(reloaded.Id, patientUserId);
            }
            return Result<PaymentReceiptDto>.Failure("Concurrent update conflict during payment processing.");
        }

        // 7. Send notification
        try
        {
            var patientUserId = appointment.Patient?.UserId ?? string.Empty;
            if (!string.IsNullOrEmpty(patientUserId))
            {
                await _notificationService.SendNotificationAsync(
                    patientUserId,
                    "Payment Confirmed",
                    $"Your payment of {expectedPayable:F2} EGP for appointment #{appointment.Id} was confirmed via Paymob. Ref: {txnRef}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for Paymob payment {TxnRef}", txnRef);
        }

        return Result<PaymentReceiptDto>.Success(new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = expectedPayable,
            OriginalAmount = originalFee,
            DiscountAmount = discountAmount,
            AppliedPromoCode = appointment.PromoCode,
            PaidAt = paidTime,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = "Paymob Egypt (Online Card)"
        });
    }
}
