using TestPlans.Domain;

namespace TestPlans.Application.Abstractions;

/// <summary>
/// Records an action against a task: upserts the current <see cref="TaskResult"/> for its
/// (task, platform, version) and appends an <see cref="ActionLogEntry"/> — the shared write path behind
/// both the primary-native "record result" endpoint and the Guide sync. Staging only; the unit of work
/// owns SaveChanges.
/// </summary>
public interface ITaskResultStore
{
    Task RecordAsync(
        Guid actionId,
        Guid testTaskId,
        Guid platformId,
        Guid versionId,
        TaskResultStatus status,
        string actorId,
        ActionSource source,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken);
}
