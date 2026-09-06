namespace Fleptix.Observer.Hubs;

using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Real-time SignalR hub broadcasting container telemetry, status transitions, and log streams.
/// </summary>
public class ContainerHub : Hub
{
    public async Task JoinContainerRoom(string containerId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"container_{containerId}");
    }

    public async Task LeaveContainerRoom(string containerId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"container_{containerId}");
    }
}
