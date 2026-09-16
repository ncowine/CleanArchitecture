namespace BuildingBlocks.RealTime;

/// <summary>
/// Canonical realtime group names, shared by publishers and the hub so both agree on the key without a
/// direct dependency on each other.
/// </summary>
public static class RealtimeGroups
{
    /// <summary>Everyone watching equipment inventory changes.</summary>
    public static string Equipment() => "equipment";
}
