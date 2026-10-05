using System.Security.Claims;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsApiController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsApiController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// GET /api/notifications/unread
    /// Retrieves unread alerts for current authenticated user.
    /// </summary>
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnread()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.GetUnreadNotificationsAsync(userId);
        return Ok(new
        {
            success = true,
            data = result.Value ?? new()
        });
    }

    /// <summary>
    /// POST /api/notifications/mark-read/{id}
    /// Marks an alert as read, validating ownership.
    /// </summary>
    [HttpPost("mark-read/{id:int}")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.MarkAsReadAsync(id, userId);
        if (!result.IsSuccess)
        {
            return StatusCode(403, new
            {
                success = false,
                error = result.Error
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                id,
                isRead = true
            }
        });
    }
}
