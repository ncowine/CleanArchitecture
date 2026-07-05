namespace BuildingBlocks.RealTime;

/// <summary>
/// Canonical realtime group names, shared by publishers and the hub so both agree on the key without a
/// direct dependency on each other.
/// </summary>
public static class RealtimeGroups
{
    /// <summary>The group of everyone viewing/working a single guide config.</summary>
    public static string Config(Guid configId) => $"config:{configId}";
}
