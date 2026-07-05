using BuildingBlocks.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace CleanArch.Api.Realtime;

/// <summary>
/// Real-time presence + notifications hub. A client calls <c>JoinConfig</c> to subscribe to a guide config's
/// activity; while subscribed it receives <c>TaskActioned</c> events (someone actioned a task in that config)
/// and <c>presence</c> updates (who is currently working the config). Presence is best-effort and tracked
/// per connection.
/// </summary>
public sealed class PresenceHub : Hub
{
    private readonly IPresenceTracker _presence;

    public PresenceHub(IPresenceTracker presence)
    {
        _presence = presence;
    }

    public async Task JoinConfig(Guid configId)
    {
        var group = RealtimeGroups.Config(configId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _presence.Join(group, Context.ConnectionId, CurrentUser());
        await BroadcastPresenceAsync(group);
    }

    public async Task LeaveConfig(Guid configId)
    {
        var group = RealtimeGroups.Config(configId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _presence.Leave(Context.ConnectionId);
        await BroadcastPresenceAsync(group);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var groups = _presence.GroupsFor(Context.ConnectionId);
        _presence.Leave(Context.ConnectionId);
        foreach (var group in groups)
        {
            await BroadcastPresenceAsync(group);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastPresenceAsync(string group) =>
        Clients.Group(group).SendAsync("presence", new { group, users = _presence.UsersIn(group) });

    private string CurrentUser() =>
        Context.User?.Identity is { IsAuthenticated: true, Name: { } name } ? name : "anonymous";
}
