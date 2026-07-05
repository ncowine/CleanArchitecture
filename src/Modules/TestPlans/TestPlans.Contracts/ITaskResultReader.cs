namespace TestPlans.Contracts;

/// <summary>
/// The current status of a task for a (platform, version) as held by the primary system — the source of
/// truth. <c>Status</c> is one of <c>CheckedOut | Pass | Fail | Skip</c>, or the whole snapshot is null
/// when the task has never been actioned for that (platform, version).
/// </summary>
public sealed record TaskStatusSnapshot(
    Guid TestTaskId,
    Guid PlatformId,
    Guid TestPlanVersionId,
    string Status,
    string ActorId,
    DateTime ActionedOnUtc);

/// <summary>
/// Published read of a task's current result from the primary system. Lets the Tester Guide show whether a
/// task was already actioned elsewhere without depending on the Test Plans domain or DbContext.
/// </summary>
public interface ITaskResultReader
{
    Task<TaskStatusSnapshot?> GetAsync(
        Guid testTaskId, Guid platformId, Guid versionId, CancellationToken cancellationToken);
}
