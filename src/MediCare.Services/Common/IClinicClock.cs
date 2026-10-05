namespace MediCare.Services.Common;

/// <summary>
/// Abstraction for providing the current clinic local wall-clock time.
/// Time Zone Assumption:
/// MediCare operates as an outpatient medical clinic in Egypt. All working hours, doctor leaves,
/// appointment schedules, and slot availability evaluations are strictly anchored to Local Clinic Time
/// (Egypt Standard Time, UTC+2 / UTC+3). Database columns for appointment dates and working hour times
/// represent local clinic wall-clock time.
/// </summary>
public interface IClinicClock
{
    DateTime Now { get; }
    DateTime Today { get; }
}

public class ClinicClock : IClinicClock
{
    public DateTime Now => DateTime.Now;
    public DateTime Today => DateTime.Today;
}
