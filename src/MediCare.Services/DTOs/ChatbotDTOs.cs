namespace MediCare.Services.DTOs;

public class ChatbotRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string? Culture { get; set; } = "en";
}

public class ChatbotResponseDto
{
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyMessage => ReplyText;
    public string? RecommendedSpecialization { get; set; }
    public string? SpecialtySuggested => RecommendedSpecialization;
    public bool IsEmergency { get; set; }
    public string? EmergencyNotice { get; set; }
    public List<ChatbotDoctorCardDto> Doctors { get; set; } = new();
    public List<ChatbotDoctorCardDto> RecommendedDoctors => Doctors;
    public List<string> QuickReplies { get; set; } = new();
}

public class ChatbotDoctorCardDto
{
    public int Id { get; set; }
    public int DoctorId => Id;
    public string FullName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal ConsultationFee => Fee;
    public string? AvatarUrl { get; set; }
    public string? ProfileImageUrl => AvatarUrl;
}
