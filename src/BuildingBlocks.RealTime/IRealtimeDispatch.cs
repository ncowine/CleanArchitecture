namespace BuildingBlocks.RealTime;

/// <summary>
/// Collects realtime events to publish <b>after</b> the current request commits. Handlers call
/// <see cref="Publish"/> while handling; the <see cref="RealtimeDispatchBehavior{TRequest,TResponse}"/>
/// flushes them once the inner pipeline (including the database transaction) has completed successfully — so
/// a client is never notified about a change that then rolled back.
/// </summary>
public interface IRealtimeDispatch
{
    void Publish(string group, RealtimeEvent realtimeEvent);
}
