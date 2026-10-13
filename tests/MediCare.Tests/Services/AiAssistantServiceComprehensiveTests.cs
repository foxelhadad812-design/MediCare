using System.Net;
using System.Text;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AiAssistantServiceComprehensiveTests
{
    private readonly Mock<ILogger<OpenAiAssistantService>> _loggerMock = new();
    private readonly Mock<ILogger<AiChatbotService>> _chatbotLoggerMock = new();

    private sealed class MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return sendFunc(request, cancellationToken);
        }
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenConsentNotGiven_MakesZeroExternalCalls_AndReturnsConsentNotice()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "I have mild knee pain when jogging",
            Culture = "en",
            ConsentGiven = false // User has NOT checked the consent box
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0, "zero external HTTP calls must occur when consent is not explicitly granted");
        result.IsConsentRequiredNotice.Should().BeTrue();
        result.ReplyText.Should().Contain("Explicit consent is required");
        result.ProviderUsed.Should().Be("local");
        result.Disclaimer.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenEmergencySymptomDetected_ReturnsUrgentTriage_WithoutCallingExternalProvider()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "Severe chest pain radiating to left arm and cannot breathe",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0, "acute emergency symptoms must trigger instant local triage without external provider latency");
        result.IsEmergency.Should().BeTrue();
        result.ReplyText.Should().Contain("123");
        result.ReplyText.Should().Contain("Emergency Room");
        result.ProviderUsed.Should().Be("local_safety_guard");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenArabicEmergencyDetected_ReturnsUrgentArabicTriage()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "أشعر بألم شديد بالصدر وضيق تنفس حاد ومفاجئ",
            Culture = "ar",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0);
        result.IsEmergency.Should().BeTrue();
        result.ReplyText.Should().Contain("123");
        result.ReplyText.Should().Contain("طوارئ");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenMedicationPrescriptionRequested_RefusesSafely()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "Please prescribe me an antibiotic dosage for throat pain",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0, "medication/prescription requests must be rejected immediately on the server");
        result.IsEmergency.Should().BeFalse();
        result.ReplyText.Should().Contain("not permitted to prescribe medications");
        result.ProviderUsed.Should().Be("local_safety_guard");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenPiiPresent_RedactsPiiFromTransmittedPayload()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        string? capturedPayload = null;

        var handler = new MockHttpMessageHandler(async (req, token) =>
        {
            capturedRequest = req;
            capturedPayload = await req.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"reply_text\":\"Dermatology consultation suggested.\",\"recommended_specialization\":\"Dermatology\"}"}]}]}
                    """, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "My national ID is 29801011234567 and my phone is 01012345678, I have a skin rash",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(1);
        result.PiiRedacted.Should().BeTrue();
        capturedPayload.Should().NotContain("29801011234567", "raw National ID must be redacted before sending to external AI");
        capturedPayload.Should().NotContain("01012345678", "raw Phone Number must be redacted before sending to external AI");
        capturedPayload.Should().Contain("[REDACTED_NATIONAL_ID]");
        capturedPayload.Should().Contain("[REDACTED_PHONE]");
        result.RecommendedSpecialization.Should().Be("Dermatology");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenApiKeyMissing_ReturnsUnconfiguredNotice_WithoutExternalCall()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = null, Provider = "OpenAI" }); // No key configured
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "I have mild knee pain when running",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0);
        result.ProviderUsed.Should().Be("unconfigured_local");
        result.ReplyText.Should().Contain("unconfigured");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenProviderReturnsHttpError_EngagesSafeFallback()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "I have ear pain",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        result.ProviderUsed.Should().Be("local_fallback");
        result.ReplyText.Should().Contain("Unable to reach the external AI service");
    }

    [Fact]
    public async Task GenerateGuidanceAsync_WhenAzureOpenAiConfigured_SendsToAzureEndpointWithApiKeyHeader()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":"Orthopedic consultation recommended."}}]}
                    """, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings
        {
            Provider = "AzureOpenAI",
            ApiKey = "azure-secret-key",
            Endpoint = "https://medicare-eastus.openai.azure.com",
            Model = "gpt-4o-deployment"
        });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = "Joint pain in shoulder",
            Culture = "en",
            ConsentGiven = true
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(1);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsoluteUri.Should().Contain("medicare-eastus.openai.azure.com");
        capturedRequest.Headers.Contains("api-key").Should().BeTrue();
        capturedRequest.Headers.GetValues("api-key").First().Should().Be("azure-secret-key");
        result.ProviderUsed.Should().Be("azure_openai");
        result.ReplyText.Should().Contain("Orthopedic consultation recommended");
    }

    [Fact]
    public async Task AiChatbotService_WhenConsentNotGiven_ReturnsLocalDemoMode_AndConsentRequiredFlag()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ChatbotTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        using var dbContext = new ApplicationDbContext(options);
        var uow = new UnitOfWork(dbContext);
        var localChatbot = new ChatbotService(uow);

        var mockAiAssistant = new Mock<IAiAssistantService>();

        var service = new AiChatbotService(localChatbot, mockAiAssistant.Object, _chatbotLoggerMock.Object);

        var request = new ChatbotRequestDto
        {
            Message = "عندي وجع خفيف في ركبتي",
            Culture = "ar",
            ConsentGiven = false
        };

        // Act
        var result = await service.ProcessMessageAsync(request);

        // Assert
        mockAiAssistant.Verify(a => a.GenerateGuidanceAsync(It.IsAny<AiAssistantRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        result.ConsentRequired.Should().BeTrue();
        result.EngineMode.Should().Be("local_demo");
    }

    [Theory]
    [InlineData("Chest pain and shortness of breath", "en")]
    [InlineData("Severe chest pain radiating to left arm", "en")]
    [InlineData("وجع في الصدر وضيق تنفس", "ar")]
    [InlineData("ألم بصدري وضيق في التنفس", "ar")]
    public async Task OpenAiAssistantService_WhenEmergencyDetected_BypassesConsentCheck_AndMakesZeroHttpCalls(string emergencyInput, string culture)
    {
        // Arrange
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiAssistantSettings { ApiKey = "valid-test-key", Provider = "OpenAI" });
        var service = new OpenAiAssistantService(httpClient, settings, _loggerMock.Object);

        var request = new AiAssistantRequestDto
        {
            Message = emergencyInput,
            Culture = culture,
            ConsentGiven = false // Note: Consent is FALSE, yet emergency must STILL trigger!
        };

        // Act
        var result = await service.GenerateGuidanceAsync(request);

        // Assert
        handler.CallCount.Should().Be(0, "Emergency triage must immediately short-circuit without calling external provider");
        result.IsEmergency.Should().BeTrue("Emergency flag must be set even without user consent");
        result.IsConsentRequiredNotice.Should().BeFalse("Consent notice must NOT displace emergency guidance");
        result.ReplyText.Should().Contain("123");
        result.ProviderUsed.Should().Be("local_safety_guard");
    }

    [Fact]
    public async Task AiChatbotService_WhenEmergencyDetected_ReturnsEmergencyResponseImmediately_EvenIfFallbackFails()
    {
        // Arrange
        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Doctors.GetApprovedDoctorsAsync(null))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var localChatbot = new ChatbotService(mockUow.Object);
        var mockAiAssistant = new Mock<IAiAssistantService>();

        var service = new AiChatbotService(localChatbot, mockAiAssistant.Object, _chatbotLoggerMock.Object);

        var request = new ChatbotRequestDto
        {
            Message = "Chest pain and shortness of breath",
            Culture = "en",
            ConsentGiven = false
        };

        // Act
        var result = await service.ProcessMessageAsync(request);

        // Assert
        result.IsEmergency.Should().BeTrue();
        result.EngineMode.Should().Be("emergency_triage");
        result.EmergencyNotice.Should().Contain("123");
        mockAiAssistant.Verify(a => a.GenerateGuidanceAsync(It.IsAny<AiAssistantRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AiChatbotService_WhenFallbackThrows_RecoversWithResilientResponse()
    {
        // Arrange
        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Doctors.GetApprovedDoctorsAsync(null))
            .ThrowsAsync(new InvalidOperationException("Simulated DB connection timeout"));

        var localChatbot = new ChatbotService(mockUow.Object);
        var mockAiAssistant = new Mock<IAiAssistantService>();

        var service = new AiChatbotService(localChatbot, mockAiAssistant.Object, _chatbotLoggerMock.Object);

        var request = new ChatbotRequestDto
        {
            Message = "Cardiology doctor booking",
            Culture = "en",
            ConsentGiven = false
        };

        // Act
        var result = await service.ProcessMessageAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ReplyText.Should().NotBeNullOrEmpty();
        result.EngineMode.Should().Be("local_demo");
    }

    [Fact]
    public void PatientDataPrivacyGuard_CorrectlyIdentifiesAndRedactsMultiplePiiTypes()
    {
        // Arrange
        string raw = "Contact me at 01012345678 or john.doe@example.com, my ID is 30101012345678 and card is 4532-0150-1234-5678";

        // Act
        var sanitized = PatientDataPrivacyGuard.SanitizeInput(raw);

        // Assert
        sanitized.ContainsPii.Should().BeTrue();
        sanitized.DetectedPiiTypes.Should().Contain(new[] { "Phone Number", "Email Address", "National ID", "Payment Card Number" });
        sanitized.SanitizedMessage.Should().NotContain("01012345678");
        sanitized.SanitizedMessage.Should().NotContain("john.doe@example.com");
        sanitized.SanitizedMessage.Should().NotContain("30101012345678");
        sanitized.SanitizedMessage.Should().NotContain("4532-0150-1234-5678");
    }
}
