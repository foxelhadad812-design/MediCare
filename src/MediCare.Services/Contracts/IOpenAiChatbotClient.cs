namespace MediCare.Services.Contracts;

public interface IOpenAiChatbotClient
{
    Task<OpenAiChatbotResult?> GetGuidanceAsync(string message, string culture, CancellationToken cancellationToken = default);
}

public sealed record OpenAiChatbotResult(string ReplyText, string? RecommendedSpecialization);
