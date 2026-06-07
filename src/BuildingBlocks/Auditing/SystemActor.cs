namespace BuildingBlocks.Auditing;

/// <summary>Default actor when the host hasn't supplied one — e.g. background work, or no auth yet.</summary>
internal sealed class SystemActor : ICurrentActor
{
    public string Current => "system";
}
