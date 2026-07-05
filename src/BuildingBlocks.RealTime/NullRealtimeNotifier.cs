namespace BuildingBlocks.RealTime;

/// <summary>No-op default used when no transport is wired. The host overrides it with a real one.</summary>
internal sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
