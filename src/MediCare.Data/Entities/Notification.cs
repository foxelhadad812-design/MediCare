namespace MediCare.Data.Entities;

public class Notification : BaseAuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;

    public virtual ApplicationUser User { get; set; } = null!;
}
