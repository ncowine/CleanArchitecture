namespace BuildingBlocks.Outbox;

/// <summary>
/// Requeues a dead-lettered outbox message for another delivery attempt — the manual recovery step
/// after the underlying cause has been fixed (a missing handler added, a downstream outage resolved).
/// </summary>
public interface IOutboxReplayer
{
    /// <summary>
    /// Clears the dead-letter state on the message so the dispatcher will pick it up again. Returns
    /// false if no dead-lettered message with that id exists (already replayed, or never dead-lettered).
    /// </summary>
    Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken);
}
