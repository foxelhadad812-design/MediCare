using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediCare.Services.Common;

namespace MediCare.Services.Implementations;

public sealed class AiChatbotService : IChatbotService
{
    private readonly ChatbotService _fallback;
    private readonly IOpenAiChatbotClient _aiClient;
    private readonly OpenAiChatbotSettings _settings;
    private readonly ILogger<AiChatbotService> _logger;

    public AiChatbotService(ChatbotService fallback, IOpenAiChatbotClient aiClient,
        IOptions<OpenAiChatbotSettings> settings, ILogger<AiChatbotService> logger)
    {
        _fallback = fallback;
        _aiClient = aiClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request)
    {
        var fallback = await _fallback.ProcessMessageAsync(request);
        if (fallback.IsEmergency || string.IsNullOrWhiteSpace(_settings.ApiKey)) return fallback;

        try
        {
            var ai = await _aiClient.GetGuidanceAsync(request.Message, request.Culture ?? "en");
            if (ai is null) return fallback;

            fallback.ReplyText = ai.ReplyText;
            fallback.RecommendedSpecialization = ai.RecommendedSpecialization;
            fallback.EngineMode = "openai";
            if (!string.IsNullOrWhiteSpace(ai.RecommendedSpecialization) && fallback.Doctors.Count == 0)
            {
                var doctorSuggestions = await _fallback.ProcessMessageAsync(new ChatbotRequestDto
                {
                    Message = ai.RecommendedSpecialization,
                    Culture = request.Culture
                });
                fallback.Doctors = doctorSuggestions.Doctors;
            }
            return fallback;
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("AI clinic guide request failed; using the local fallback. Error type: {ErrorType}", exception.GetType().Name);
            return fallback;
        }
    }
}
