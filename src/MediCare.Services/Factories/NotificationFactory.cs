using MediCare.Data.Entities;
using MediCare.Services.DTOs;

namespace MediCare.Services.Factories;

public class NotificationFactory
{
    public Notification CreateNotification(string userId, string title, string message)
    {
        return new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            IsRead = false
        };
    }

    public NotificationDto CreatePayload(Notification entity)
    {
        return new NotificationDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Title = entity.Title,
            Message = entity.Message,
            CreatedAt = entity.CreatedAt,
            IsRead = entity.IsRead
        };
    }
}
