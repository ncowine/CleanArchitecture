using BuildingBlocks.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CleanArch.Api.Realtime;

/// <summary>
/// Real-time presence + notifications hub. A client calls <c>JoinGroup</c> with one of
/// <see cref="RealtimeGroups"/>'s names (e.g. <c>RealtimeGroups.Equipment()</c>) to subscribe to that
/// group's activity; while subscribed it receives that group's events (e.g. <c>EquipmentCreated</c>) and
/// <c>presence</c> updates (who is currently connected to the group). Presence is best-effort and tracked
/// per connection.
/// <para>
/// <b>Callers must be authenticated</b>, exactly as for the API's write endpoints: the default scheme picks API key,
/// Okta bearer token (when configured) or AD Basic from the request's headers. Without valid credentials the
/// negotiate request is refused with 401 and no connection is made. The .NET SignalR client (the desktop apps) sends
/// its headers on the negotiate request and on the WebSocket itself.
/// </para>
/// <para>
/// Browsers can't set headers on a WebSocket. A browser client using Okta would pass its token as the
/// <c>access_token</c> query parameter, and the JWT bearer options would need an <c>OnMessageReceived</c> hook that
/// reads it for <c>/hubs</c> paths — the standard ASP.NET Core SignalR set-up, not added yet.
/// </para>
/// </summary>
[Authorize]
public sealed class PresenceHub : Hub
{
    private readonly IPresenceTracker _presence;

    public PresenceHub(IPresenceTracker presence)
    {
        _presence = presence;
    }

    public async Task JoinGroup(string group)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _presence.Join(group, Context.ConnectionId, CurrentUser());
        await BroadcastPresenceAsync(group);
    }

    public async Task LeaveGroup(string group)
    {
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
