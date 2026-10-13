namespace MediCare.Services.Common;

/// <summary>
/// Configuration settings for Paymob Egypt payment gateway integration.
/// Sensitive credentials (ApiKey, HmacSecret) must be loaded from User Secrets
/// or environment variables, never committed to source control.
/// </summary>
public class PaymobSettings
{
    public const string SectionName = "Paymob";

    /// <summary>
    /// Gateway provider mode: "Paymob" or "Simulated" (allowed only in Development).
    /// </summary>
    public string Provider { get; set; } = "Simulated";

    /// <summary>
    /// Paymob Secret API Key for authenticating API requests.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Paymob HMAC Secret key used to cryptographically verify incoming webhook signatures.
    /// </summary>
    public string? HmacSecret { get; set; }

    /// <summary>
    /// Paymob Card Integration ID configured in the Paymob dashboard.
    /// </summary>
    public int IntegrationId { get; set; }

    /// <summary>
    /// Paymob Iframe ID for hosted card checkout.
    /// </summary>
    public string? IframeId { get; set; }

    /// <summary>
    /// Paymob API Base URL (defaults to production endpoint).
    /// </summary>
    public string BaseUrl { get; set; } = "https://accept.paymob.com/api";

    /// <summary>
    /// Standard currency code (defaults to EGP for Egypt).
    /// </summary>
    public string Currency { get; set; } = "EGP";

    /// <summary>
    /// Returns true if all mandatory Paymob production/sandbox credentials are configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(HmacSecret) &&
        IntegrationId > 0 &&
        !string.IsNullOrWhiteSpace(IframeId);
}
