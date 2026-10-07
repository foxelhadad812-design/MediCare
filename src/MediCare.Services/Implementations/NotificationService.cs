using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

/// <summary>
/// Service managing user notifications.
/// Enforces the requirement to persist all alerts to the database FIRST before pushing
/// across real-time SignalR websockets.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _uow;
    private readonly NotificationFactory _factory;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IUnitOfWork uow,
        NotificationFactory factory,
        IRealtimeNotifier realtimeNotifier,
        ILogger<NotificationService> logger)
    {
        _uow = uow;
        _factory = factory;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<Result<List<NotificationDto>>> GetUnreadNotificationsAsync(string userId)
    {
        var entities = (await _uow.Notifications.FindAsync(n => n.UserId == userId && !n.IsRead))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => _factory.CreatePayload(n))
            .ToList();

        return Result<List<NotificationDto>>.Success(entities);
    }

    public async Task<Result> MarkAsReadAsync(int notificationId, string currentUserId)
    {
        var notification = await _uow.Notifications.GetByIdAsync(notificationId);
        if (notification == null)
        {
            return Result.Failure("Notification not found.");
        }

        if (notification.UserId != currentUserId)
        {
            _logger.LogWarning("Security violation: User {CurrentUserId} attempted to mark notification {NotificationId} owned by {OwnerId}",
                currentUserId, notificationId, notification.UserId);
            return Result.Failure("Access denied: You do not own this notification.");
        }

        notification.IsRead = true;
        _uow.Notifications.Update(notification);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result> SendNotificationAsync(string userId, string title, string message)
    {
        try
        {
            // 1. Persist to database FIRST
            var entity = _factory.CreateNotification(userId, title, message);
            await _uow.Notifications.AddAsync(entity);
            await _uow.CommitAsync();

            // 2. Push through real-time notifier
            var payload = _factory.CreatePayload(entity);
            await _realtimeNotifier.PushNotificationToUserAsync(userId, payload);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist and dispatch notification to user {UserId}", userId);
            return Result.Failure("Failed to deliver notification.");
        }
    }

    public async Task NotifySlotAvailabilityChangedAsync(int doctorId, DateTime date)
    {
        try
        {
            await _realtimeNotifier.NotifySlotChangedAsync(doctorId, date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting slot change for doctor {DoctorId}", doctorId);
        }
    }

    public async Task NotifyAppointmentStatusChangedAsync(int appointmentId, string newStatus, string targetUserId)
    {
        try
        {
            await _realtimeNotifier.NotifyStatusChangedAsync(targetUserId, appointmentId, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting status change for appointment {AppointmentId}", appointmentId);
        }
    }
}
