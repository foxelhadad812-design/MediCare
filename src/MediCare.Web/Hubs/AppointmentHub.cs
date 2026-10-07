using System.Security.Claims;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MediCare.Web.Hubs;

[Authorize]
public class AppointmentHub : Hub<IAppointmentNotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinUserGroup(string userId)
    {
        var currentUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
    }

    public async Task LeaveUserGroup(string userId)
    {
        var currentUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
    }
}
