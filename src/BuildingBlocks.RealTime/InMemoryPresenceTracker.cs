using System.Collections.Concurrent;

namespace BuildingBlocks.RealTime;

/// <summary>
/// Single-node, in-memory <see cref="IPresenceTracker"/>. Fine for one process; swap for a Redis-backed
/// implementation to share presence across a scaled-out deployment.
/// </summary>
public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    // group -> (connectionId -> user)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _groups = new();

    // connectionId -> set of groups it joined (so a disconnect can clean up every group)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections = new();

    public void Join(string group, string connectionId, string user)
    {
        _groups.GetOrAdd(group, _ => new ConcurrentDictionary<string, string>())[connectionId] = user;
        _connections.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>())[group] = 0;
    }

    public void Leave(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var groups))
        {
            return;
        }

        foreach (var group in groups.Keys)
        {
            if (_groups.TryGetValue(group, out var members))
            {
                members.TryRemove(connectionId, out _);
            }
        }
    }

    public IReadOnlyList<string> UsersIn(string group) =>
        _groups.TryGetValue(group, out var members)
            ? members.Values.Distinct().ToList()
            : Array.Empty<string>();

    public IReadOnlyList<string> GroupsFor(string connectionId) =>
        _connections.TryGetValue(connectionId, out var groups)
            ? groups.Keys.ToList()
            : Array.Empty<string>();
}
