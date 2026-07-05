namespace BuildingBlocks.RealTime;

/// <summary>
/// Scoped, per-request collector of pending realtime events. Public (not just <see cref="IRealtimeDispatch"/>)
/// because the dispatch behavior drains it; handlers should depend on the interface.
/// </summary>
public sealed class RealtimeDispatch : IRealtimeDispatch
{
    private readonly List<(string Group, RealtimeEvent Event)> _pending = new();

    public void Publish(string group, RealtimeEvent realtimeEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(realtimeEvent);
        _pending.Add((group, realtimeEvent));
    }

    /// <summary>Returns and clears the pending events. Called by the dispatch behavior after a successful commit.</summary>
    public IReadOnlyList<(string Group, RealtimeEvent Event)> Drain()
    {
        if (_pending.Count == 0)
        {
            return Array.Empty<(string, RealtimeEvent)>();
        }

        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }
}
