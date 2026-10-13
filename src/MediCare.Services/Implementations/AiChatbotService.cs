using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public sealed class AiChatbotService : IChatbotService
{
    private readonly ChatbotService _fallback;
    private readonly IAiAssistantService _aiAssistant;
    private readonly ILogger<AiChatbotService> _logger;

    public AiChatbotService(
        ChatbotService fallback,
        IAiAssistantService aiAssistant,
        ILogger<AiChatbotService> logger)
    {
        _fallback = fallback;
        _aiAssistant = aiAssistant;
        _logger = logger;
    }

    public async Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request)
    {
        // 1. In-memory deterministic emergency check FIRST (zero database or network latency)
        if (MedicalSafetyGuard.IsEmergencySymptom(request.Message))
        {
            var isAr = request.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
            return new ChatbotResponseDto
            {
                IsEmergency = true,
                EmergencyNotice = MedicalSafetyGuard.GetEmergencyGuidance(request.Culture),
                ReplyText = isAr
                    ? "⚠️ تنبيه طوارئ عاجل: الأعراض المذكورة قد تشير إلى حالة حرجة. توجه فوراً لأقرب قسم طوارئ أو اتصل بالإسعاف على 123."
                    : "⚠️ CRITICAL EMERGENCY NOTICE: The symptoms described may indicate an urgent medical emergency. Please call 123 (or 911) or proceed immediately to the nearest Emergency Room.",
                EngineMode = "emergency_triage",
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(request.Culture),
                QuickReplies = isAr
                    ? new() { "أرقام الطوارئ", "تواصل مع المستشفى" }
                    : new() { "Emergency Numbers", "Contact Hospital" }
            };
        }

        // 2. Resilient fallback execution
        ChatbotResponseDto fallback;
        try
        {
            fallback = await _fallback.ProcessMessageAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Fallback chatbot processing failed: {Message}. Returning safe resilient response.", ex.Message);
            var isAr = request.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
            fallback = new ChatbotResponseDto
            {
                ReplyText = isAr
                    ? "شكراً لتواصلك مع MediCare! يمكنك تصفح التخصصات وحجز المواعيد مع الأطباء عبر الموقع."
                    : "Welcome to MediCare! You can browse doctor specialties and book appointments directly through the portal.",
                EngineMode = "local_demo",
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(request.Culture),
                QuickReplies = isAr
                    ? new() { "ابحث عن طبيب", "مواعيدي", "طريقة الحجز" }
                    : new() { "Find Doctors", "My Appointments", "How to Book" }
            };
        }

        // If fallback flagged an emergency, return immediately
        if (fallback.IsEmergency)
        {
            return fallback;
        }

        // 3. If consent is not given, return fallback with a notice that consent is required for external AI
        if (!request.ConsentGiven)
        {
            fallback.ConsentRequired = true;
            fallback.EngineMode = "local_demo";
            fallback.Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(request.Culture);
            return fallback;
        }

        // 4. Delegate to the configurable AI Assistant Service (OpenAI / Azure OpenAI)
        try
        {
            var aiRequest = new AiAssistantRequestDto
            {
                Message = request.Message,
                Culture = request.Culture,
                ConsentGiven = request.ConsentGiven
            };

            var aiResponse = await _aiAssistant.GenerateGuidanceAsync(aiRequest);

            fallback.ReplyText = aiResponse.ReplyText;
            fallback.RecommendedSpecialization = aiResponse.RecommendedSpecialization;
            fallback.EngineMode = aiResponse.ProviderUsed;
            fallback.IsEmergency = aiResponse.IsEmergency;
            if (aiResponse.IsEmergency)
            {
                fallback.EmergencyNotice = aiResponse.ReplyText;
            }
            fallback.PiiDetected = aiResponse.PiiRedacted;
            fallback.Disclaimer = aiResponse.Disclaimer ?? MedicalSafetyGuard.GetStandardDisclaimer(request.Culture);

            // Fetch matching doctors from local database for the recommended specialty
            if (!string.IsNullOrWhiteSpace(aiResponse.RecommendedSpecialization) && fallback.Doctors.Count == 0)
            {
                try
                {
                    var doctorSuggestions = await _fallback.ProcessMessageAsync(new ChatbotRequestDto
                    {
                        Message = aiResponse.RecommendedSpecialization,
                        Culture = request.Culture
                    });
                    fallback.Doctors = doctorSuggestions.Doctors;
                }
                catch
                {
                    // Doctor recommendation degradation is non-fatal
                }
            }

            return fallback;
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AI assistant request encountered an issue. Using local fallback. Error type: {ErrorType}", ex.GetType().Name);
            return fallback;
        }
    }
}
