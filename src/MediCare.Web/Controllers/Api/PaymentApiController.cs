using System.Security.Claims;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/payment")]
[Authorize]
public class PaymentApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentApiController> _logger;

    public PaymentApiController(
        IPaymentService paymentService,
        ILogger<PaymentApiController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost("checkout")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Checkout([FromBody] PaymentCheckoutRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymentService.ProcessCheckoutAsync(request, userId);
        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            receipt = result.Value
        });
    }

    [HttpGet("receipt/{appointmentId:int}")]
    public async Task<IActionResult> GetReceipt(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymentService.GetReceiptAsync(appointmentId, userId);
        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            receipt = result.Value
        });
    }

    [HttpPost("validate-promo")]
    public async Task<IActionResult> ValidatePromo([FromBody] PromoValidationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { success = false, error = "Promo code is required." });
        }

        var result = await _paymentService.ValidatePromoCodeAsync(request.Code, request.Amount);
        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error ?? "Invalid promo code." });
        }

        return Ok(new
        {
            success = true,
            data = result.Value
        });
    }

    [HttpPost("initiate")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> InitiatePayment(
        [FromBody] PaymentInitiateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || request.AppointmentId <= 0)
        {
            return BadRequest(new { success = false, error = "Valid appointment ID is required." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { success = false, error = "Authentication required." });
        }

        var result = await _paymentService.InitiateAppointmentPaymentAsync(
            request.AppointmentId,
            request.PromoCode,
            userId,
            cancellationToken);

        if (!result.IsSuccess || result.Value == null)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            data = result.Value
        });
    }

    [HttpPost("webhook/paymob")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymobWebhook(
        [FromBody] System.Text.Json.JsonElement payload,
        [FromQuery] string? hmac = null,
        CancellationToken cancellationToken = default)
    {
        // Extract HMAC from Query string, Header, or Payload
        var receivedHmac = hmac;
        if (string.IsNullOrWhiteSpace(receivedHmac) && Request?.Headers != null && Request.Headers.TryGetValue("X-Paymob-Hmac", out var headerHmac))
        {
            receivedHmac = headerHmac.ToString();
        }

        if (string.IsNullOrWhiteSpace(receivedHmac) && payload.TryGetProperty("hmac", out var bodyHmac))
        {
            receivedHmac = bodyHmac.GetString();
        }

        if (string.IsNullOrWhiteSpace(receivedHmac))
        {
            _logger.LogWarning("Paymob webhook rejected: Missing HMAC signature.");
            return BadRequest(new { success = false, error = "Missing HMAC signature." });
        }

        // Handle both root object and nested obj wrapper
        var targetObj = payload.TryGetProperty("obj", out var objProp) ? objProp : payload;

        long id = targetObj.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
        bool pending = targetObj.TryGetProperty("pending", out var pProp) && pProp.GetBoolean();
        bool success = targetObj.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
        long amountCents = targetObj.TryGetProperty("amount_cents", out var aProp) ? aProp.GetInt64() : 0;
        string currency = targetObj.TryGetProperty("currency", out var cProp) ? cProp.GetString() ?? "EGP" : "EGP";
        string createdAt = targetObj.TryGetProperty("created_at", out var crProp) ? crProp.GetString() ?? string.Empty : string.Empty;
        bool errorOccured = targetObj.TryGetProperty("error_occured", out var eoProp) && eoProp.GetBoolean();
        bool hasParent = targetObj.TryGetProperty("has_parent_transaction", out var hpProp) && hpProp.GetBoolean();
        int integrationId = targetObj.TryGetProperty("integration_id", out var inProp) ? inProp.GetInt32() : 0;
        bool is3dSecure = targetObj.TryGetProperty("is_3d_secure", out var secProp) && secProp.GetBoolean();
        bool isAuth = targetObj.TryGetProperty("is_auth", out var authProp) && authProp.GetBoolean();
        bool isCapture = targetObj.TryGetProperty("is_capture", out var capProp) && capProp.GetBoolean();
        bool isRefunded = targetObj.TryGetProperty("is_refunded", out var refProp) && refProp.GetBoolean();
        bool isStandalone = targetObj.TryGetProperty("is_standalone_payment", out var standProp) && standProp.GetBoolean();
        bool isVoided = targetObj.TryGetProperty("is_voided", out var voidProp) && voidProp.GetBoolean();

        long orderId = 0;
        string? merchantOrderId = null;
        if (targetObj.TryGetProperty("order", out var orderProp))
        {
            if (orderProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (orderProp.TryGetProperty("id", out var oId)) orderId = oId.GetInt64();
                if (orderProp.TryGetProperty("merchant_order_id", out var mId)) merchantOrderId = mId.GetString();
            }
            else if (orderProp.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                orderId = orderProp.GetInt64();
            }
        }

        string? owner = targetObj.TryGetProperty("owner", out var ownerProp) ? ownerProp.GetString() : null;

        string? sourcePan = null;
        string? sourceSubType = null;
        string? sourceType = null;
        if (targetObj.TryGetProperty("source_data", out var srcProp) && srcProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (srcProp.TryGetProperty("pan", out var panProp)) sourcePan = panProp.GetString();
            if (srcProp.TryGetProperty("sub_type", out var subProp)) sourceSubType = subProp.GetString();
            if (srcProp.TryGetProperty("type", out var typeProp)) sourceType = typeProp.GetString();
        }

        var callbackDto = new PaymobTransactionCallbackDto
        {
            Id = id,
            Pending = pending,
            Success = success,
            AmountCents = amountCents,
            Currency = currency,
            CreatedAt = createdAt,
            ErrorOccured = errorOccured,
            HasParentTransaction = hasParent,
            IntegrationId = integrationId,
            Is3dSecure = is3dSecure,
            IsAuth = isAuth,
            IsCapture = isCapture,
            IsRefunded = isRefunded,
            IsStandalonePayment = isStandalone,
            IsVoided = isVoided,
            OrderId = orderId,
            MerchantOrderId = merchantOrderId,
            Owner = owner,
            SourcePan = sourcePan,
            SourceSubType = sourceSubType,
            SourceType = sourceType,
            Hmac = receivedHmac
        };

        var result = await _paymentService.ProcessWebhookCallbackAsync(callbackDto, receivedHmac, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Paymob webhook processing failed: {Error}", result.Error);
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            receipt = result.Value
        });
    }
}

public class PromoValidationRequest
{
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
