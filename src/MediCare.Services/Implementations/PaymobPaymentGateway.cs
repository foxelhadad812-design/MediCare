using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

/// <summary>
/// Production-grade Paymob Egypt payment gateway adapter implementing
/// official authentication, order registration, payment keys, hosted iframe checkout,
/// and HMAC-SHA512 cryptographic callback validation.
/// </summary>
public class PaymobPaymentGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PaymobSettings _settings;
    private readonly ILogger<PaymobPaymentGateway> _logger;

    public string GatewayName => "Paymob";

    public PaymobPaymentGateway(
        HttpClient httpClient,
        IOptions<PaymobSettings> settings,
        ILogger<PaymobPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Result<PaymentInitiationResultDto>> InitiatePaymentAsync(
        PaymentInitiationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return Result<PaymentInitiationResultDto>.Failure("Payment initiation request cannot be null.");
        }

        if (request.Amount <= 0)
        {
            return Result<PaymentInitiationResultDto>.Failure("Payable amount must be greater than zero.");
        }

        if (!_settings.IsConfigured)
        {
            _logger.LogError("Paymob payment gateway is not properly configured. Missing API credentials or integration ID.");
            return Result<PaymentInitiationResultDto>.Failure("Payment gateway is temporarily unconfigured. Please contact support.");
        }

        try
        {
            // Step 1: Obtain Authentication Token
            var authToken = await GetAuthenticationTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(authToken))
            {
                return Result<PaymentInitiationResultDto>.Failure("Failed to authenticate with Paymob payment gateway.");
            }

            // Step 2: Register Order with authoritative server amount in cents
            long amountCents = (long)Math.Round(request.Amount * 100m);
            var orderId = await RegisterOrderAsync(authToken, amountCents, request.Currency, request.MerchantOrderId, cancellationToken);
            if (orderId <= 0)
            {
                return Result<PaymentInitiationResultDto>.Failure("Failed to register order with Paymob payment gateway.");
            }

            // Step 3: Request Payment Key for Hosted Checkout
            var paymentKey = await RequestPaymentKeyAsync(authToken, orderId, amountCents, request, cancellationToken);
            if (string.IsNullOrWhiteSpace(paymentKey))
            {
                return Result<PaymentInitiationResultDto>.Failure("Failed to acquire payment token from Paymob payment gateway.");
            }

            // Step 4: Construct Hosted Checkout URL
            var checkoutUrl = $"{_settings.BaseUrl.TrimEnd('/')}/acceptance/iframes/{_settings.IframeId}?payment_token={paymentKey}";

            return Result<PaymentInitiationResultDto>.Success(new PaymentInitiationResultDto
            {
                Success = true,
                PaymentToken = paymentKey,
                CheckoutUrl = checkoutUrl,
                TransactionReference = request.TransactionReference
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Paymob payment initiation timed out or was canceled for appointment {ApptId}.", request.AppointmentId);
            return Result<PaymentInitiationResultDto>.Failure("Payment gateway request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Paymob payment initiation for appointment {ApptId}.", request.AppointmentId);
            return Result<PaymentInitiationResultDto>.Failure("An unexpected error occurred while communicating with the payment gateway.");
        }
    }

    public bool ValidateWebhookHmac(PaymobTransactionCallbackDto callback, string receivedHmac)
    {
        if (callback == null || string.IsNullOrWhiteSpace(receivedHmac))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_settings.HmacSecret))
        {
            _logger.LogError("Paymob HMAC secret is not configured. Webhook validation rejected.");
            return false;
        }

        // Official Paymob HMAC concatenated field order:
        // amount_cents + created_at + currency + error_occured + has_parent_transaction + id +
        // integration_id + is_3d_secure + is_auth + is_capture + is_refunded + is_standalone_payment +
        // is_voided + order.id + owner + pending + source_data.pan + source_data.sub_type +
        // source_data.type + success
        var builder = new StringBuilder();
        builder.Append(callback.AmountCents);
        builder.Append(callback.CreatedAt ?? string.Empty);
        builder.Append(callback.Currency ?? string.Empty);
        builder.Append(callback.ErrorOccured ? "true" : "false");
        builder.Append(callback.HasParentTransaction ? "true" : "false");
        builder.Append(callback.Id);
        builder.Append(callback.IntegrationId);
        builder.Append(callback.Is3dSecure ? "true" : "false");
        builder.Append(callback.IsAuth ? "true" : "false");
        builder.Append(callback.IsCapture ? "true" : "false");
        builder.Append(callback.IsRefunded ? "true" : "false");
        builder.Append(callback.IsStandalonePayment ? "true" : "false");
        builder.Append(callback.IsVoided ? "true" : "false");
        builder.Append(callback.OrderId);
        builder.Append(callback.Owner ?? string.Empty);
        builder.Append(callback.Pending ? "true" : "false");
        builder.Append(callback.SourcePan ?? string.Empty);
        builder.Append(callback.SourceSubType ?? string.Empty);
        builder.Append(callback.SourceType ?? string.Empty);
        builder.Append(callback.Success ? "true" : "false");

        return ComputeAndCompareHmac(builder.ToString(), receivedHmac);
    }

    public bool ValidateWebhookHmac(IDictionary<string, string> values, string receivedHmac)
    {
        if (values == null || string.IsNullOrWhiteSpace(receivedHmac))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_settings.HmacSecret))
        {
            _logger.LogError("Paymob HMAC secret is not configured. Webhook validation rejected.");
            return false;
        }

        string[] fieldOrder =
        {
            "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction",
            "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
            "is_standalone_payment", "is_voided", "order", "owner", "pending", "source_data.pan",
            "source_data.sub_type", "source_data.type", "success"
        };

        var builder = new StringBuilder();
        foreach (var field in fieldOrder)
        {
            if (values.TryGetValue(field, out var val))
            {
                builder.Append(val);
            }
        }

        return ComputeAndCompareHmac(builder.ToString(), receivedHmac);
    }

    public async Task<Result<PaymentVerificationResultDto>> VerifyPaymentAsync(
        string transactionReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
        {
            return Result<PaymentVerificationResultDto>.Failure("Transaction reference is required.");
        }

        if (!_settings.IsConfigured)
        {
            return Result<PaymentVerificationResultDto>.Failure("Paymob gateway is unconfigured.");
        }

        try
        {
            var authToken = await GetAuthenticationTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(authToken))
            {
                return Result<PaymentVerificationResultDto>.Failure("Unable to authenticate with Paymob.");
            }

            // Inquiry endpoint
            var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/acceptance/transactions/{transactionReference}";
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Paymob transaction inquiry failed with status {StatusCode}", resp.StatusCode);
                return Result<PaymentVerificationResultDto>.Failure($"Gateway returned status {resp.StatusCode}");
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            long amountCents = root.TryGetProperty("amount_cents", out var a) ? a.GetInt64() : 0;
            string currency = root.TryGetProperty("currency", out var c) ? c.GetString() ?? "EGP" : "EGP";

            return Result<PaymentVerificationResultDto>.Success(new PaymentVerificationResultDto
            {
                Success = true,
                IsPaid = success,
                TransactionReference = transactionReference,
                AmountPaid = amountCents / 100m,
                Currency = currency
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Paymob transaction {TxnRef}", transactionReference);
            return Result<PaymentVerificationResultDto>.Failure("Error verifying transaction with Paymob.");
        }
    }

    private async Task<string?> GetAuthenticationTokenAsync(CancellationToken cancellationToken)
    {
        var authEndpoint = $"{_settings.BaseUrl.TrimEnd('/')}/auth/tokens";
        var payload = JsonSerializer.Serialize(new { api_key = _settings.ApiKey });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(authEndpoint, content, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Paymob auth token request failed with status {StatusCode}", resp.StatusCode);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("token", out var tokenProp))
        {
            return tokenProp.GetString();
        }

        return null;
    }

    private async Task<long> RegisterOrderAsync(
        string authToken,
        long amountCents,
        string currency,
        string merchantOrderId,
        CancellationToken cancellationToken)
    {
        var orderEndpoint = $"{_settings.BaseUrl.TrimEnd('/')}/ecommerce/orders";
        var payload = JsonSerializer.Serialize(new
        {
            auth_token = authToken,
            delivery_needed = "false",
            amount_cents = amountCents.ToString(),
            currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency,
            merchant_order_id = merchantOrderId,
            items = Array.Empty<object>()
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(orderEndpoint, content, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Paymob order registration failed with status {StatusCode}", resp.StatusCode);
            return 0;
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
            return idProp.GetInt64();
        }

        return 0;
    }

    private async Task<string?> RequestPaymentKeyAsync(
        string authToken,
        long orderId,
        long amountCents,
        PaymentInitiationRequestDto request,
        CancellationToken cancellationToken)
    {
        var keyEndpoint = $"{_settings.BaseUrl.TrimEnd('/')}/acceptance/payment_keys";

        // Parse first and last names safely
        var nameParts = (request.CustomerFullName ?? "Patient").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "Patient";
        var lastName = nameParts.Length > 1 ? nameParts[^1] : "Customer";
        var email = !string.IsNullOrWhiteSpace(request.CustomerEmail) ? request.CustomerEmail : "patient@medicare.local";
        var phone = !string.IsNullOrWhiteSpace(request.CustomerPhone) ? request.CustomerPhone : "+201000000000";

        var payload = JsonSerializer.Serialize(new
        {
            auth_token = authToken,
            amount_cents = amountCents.ToString(),
            expiration = 3600,
            order_id = orderId.ToString(),
            billing_data = new
            {
                apartment = "NA",
                email = email,
                floor = "NA",
                first_name = firstName,
                street = "Clinic Street",
                building = "NA",
                phone_number = phone,
                shipping_method = "NA",
                postal_code = "NA",
                city = "Cairo",
                country = "EG",
                last_name = lastName,
                state = "Cairo"
            },
            currency = string.IsNullOrWhiteSpace(request.Currency) ? "EGP" : request.Currency,
            integration_id = _settings.IntegrationId
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(keyEndpoint, content, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Paymob payment key request failed with status {StatusCode}", resp.StatusCode);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("token", out var tokenProp))
        {
            return tokenProp.GetString();
        }

        return null;
    }

    private bool ComputeAndCompareHmac(string rawConcatenated, string receivedHmac)
    {
        if (string.IsNullOrWhiteSpace(rawConcatenated) || string.IsNullOrWhiteSpace(receivedHmac))
        {
            return false;
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_settings.HmacSecret!));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawConcatenated));
        var computedHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var receivedClean = receivedHmac.Trim().ToLowerInvariant();
        if (computedHex.Length != receivedClean.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(receivedClean));
    }
}
