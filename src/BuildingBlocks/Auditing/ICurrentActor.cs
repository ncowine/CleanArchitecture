namespace BuildingBlocks.Auditing;

/// <summary>
/// Supplies the actor ("who") for audit records. Stubbed until real authentication exists; the host
/// provides the implementation (e.g. from the authenticated user, or a header for now).
/// </summary>
public interface ICurrentActor
{
    string Current { get; }
}
