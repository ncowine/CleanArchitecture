namespace BuildingBlocks.RealTime;

/// <summary>
/// Tracks which users are currently present in each realtime group, keyed by an opaque connection id.
/// Transport-agnostic (no SignalR dependency): a hub calls <see cref="Join"/> on connect and
/// <see cref="Leave"/> on disconnect. In-memory by default; back it with a distributed store (e.g. Redis)
/// to scale presence across nodes.
/// </summary>
public interface IPresenceTracker
{
    void Join(string group, string connectionId, string user);

    void Leave(string connectionId);

    IReadOnlyList<string> UsersIn(string group);

    IReadOnlyList<string> GroupsFor(string connectionId);
}
