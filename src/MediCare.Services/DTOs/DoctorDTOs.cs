namespace MediCare.Services.DTOs;

public class DoctorFilterDto
{
    public int? SpecializationId { get; set; }
    public decimal? MaxFee { get; set; }
    public DayOfWeek? AvailableDay { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 6;
}

public class DoctorSummaryDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int SpecializationId { get; set; }
    public string SpecializationName { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? Bio { get; set; }
    public List<DayOfWeek> WorkingDays { get; set; } = new();
}

public class DoctorDetailDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public int SpecializationId { get; set; }
    public string SpecializationName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public int SlotDurationMinutes { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? Bio { get; set; }
    public List<WorkingHourDto> WorkingHours { get; set; } = new();
}

public class WorkingHourDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTime => $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";
}

public class SpecializationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
