namespace BuildingBlocks.RealTime;

/// <summary>
/// Transport for pushing a <see cref="RealtimeEvent"/> to everyone in a group. The abstraction keeps
/// application code free of any specific transport; the host binds it to a real one (e.g. SignalR).
/// </summary>
public interface IRealtimeNotifier
{
    Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken);
}
