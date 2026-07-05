namespace TesterGuide.Contracts;

/// <summary>
/// The Tester Guide module's published contract for the sync saga's <b>reverse leg</b>: the target the
/// primary system calls (via its outbox dispatcher) when it refuses a synced action. The implementation
/// flags the originating guide action as rejected with the reason. Idempotent in
/// <paramref name="messageId"/> (the compensation message id).
/// </summary>
public interface IGuideActionReconciler
{
    Task MarkSyncRejectedAsync(Guid messageId, Guid guideActionId, string reason, CancellationToken cancellationToken);
}
