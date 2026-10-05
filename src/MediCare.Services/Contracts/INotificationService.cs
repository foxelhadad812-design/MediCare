using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface INotificationService
{
    Task<Result<List<NotificationDto>>> GetUnreadNotificationsAsync(string userId);
    Task<Result> MarkAsReadAsync(int notificationId, string currentUserId);
    Task<Result> SendNotificationAsync(string userId, string title, string message);
    Task NotifySlotAvailabilityChangedAsync(int doctorId, DateTime date);
    Task NotifyAppointmentStatusChangedAsync(int appointmentId, string newStatus, string targetUserId);
}
