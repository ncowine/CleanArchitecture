namespace TestPlans.Contracts;

/// <summary>
/// The action a caller wants mirrored into the primary system's log. <c>Status</c> is one of
/// <c>CheckedOut | Pass | Fail | Skip</c>; <c>ActorId</c> is the tester who performed it.
/// <c>SourceReference</c> is an opaque id the caller wants echoed back if the action is rejected (it lets
/// the caller correlate a compensation to its originating record) — the primary treats it as opaque.
/// </summary>
public sealed record RecordActionInput(
    Guid TestTaskId,
    Guid PlatformId,
    Guid TestPlanVersionId,
    string Status,
    string ActorId,
    Guid SourceReference);

/// <summary>The outcome of a forward-leg delivery, so the caller can advance its own record. The primary
/// either <see cref="Recorded"/> the action or <see cref="Rejected"/> it (enqueuing a compensation back to
/// the caller). Rejection does not throw — it is a normal, expected outcome the caller must react to.</summary>
public enum ActionSyncOutcome
{
    Recorded,
    Rejected,
}

/// <summary>
/// The Test Plans module's published <b>write</b> contract: the target for the Tester Guide's
/// "sync action log to the primary DB" feature. The implementation appends an action-log entry and updates
/// the current result in one Test Plans-DB transaction, idempotent in <paramref name="messageId"/> (the
/// originating outbox message id), so a redelivery neither double-records nor double-updates.
/// <para>
/// This is the consumer end of the sync saga's forward leg. It returns whether the action was recorded or
/// rejected so the caller can mark its own record accordingly; on rejection it also publishes a compensation
/// back to the Guide (the reverse leg) when the task/version/platform no longer exists.
/// </para>
/// </summary>
public interface ITestPlanActionLog
{
    Task<ActionSyncOutcome> RecordActionAsync(Guid messageId, RecordActionInput input, CancellationToken cancellationToken);
}
