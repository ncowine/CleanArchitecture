using BuildingBlocks.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace CleanArch.Api.Realtime;

/// <summary>
/// Binds the kit's <see cref="IRealtimeNotifier"/> to SignalR: pushes the event to everyone in the group as
/// a client method named after the event type. Overrides the no-op default registered by the kit.
/// </summary>
internal sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<PresenceHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<PresenceHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
        _hub.Clients.Group(group).SendAsync(realtimeEvent.Type, realtimeEvent.Payload, cancellationToken);
}
