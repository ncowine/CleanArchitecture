namespace TestPlans.Domain;

/// <summary>
/// An append-only record that a task was actioned for a (platform, version) at a point in time — the
/// primary system's action log. The <see cref="Id"/> is supplied by the caller so a sync from the Tester
/// Guide can use the outbox message id as an idempotency key (a redelivery is a no-op).
/// </summary>
public sealed class ActionLogEntry
{
    public Guid Id { get; private set; }
    public Guid TestTaskId { get; private set; }
    public Guid PlatformId { get; private set; }
    public Guid TestPlanVersionId { get; private set; }
    public TaskResultStatus Status { get; private set; }
    public string ActorId { get; private set; } = null!;
    public DateTime OccurredOnUtc { get; private set; }
    public ActionSource Source { get; private set; }

    private ActionLogEntry() { }

    private ActionLogEntry(
        Guid id, Guid testTaskId, Guid platformId, Guid versionId,
        TaskResultStatus status, string actorId, DateTime occurredOnUtc, ActionSource source)
    {
        Id = id;
        TestTaskId = testTaskId;
        PlatformId = platformId;
        TestPlanVersionId = versionId;
        Status = status;
        ActorId = actorId;
        OccurredOnUtc = occurredOnUtc;
        Source = source;
    }

    public static ActionLogEntry Create(
        Guid id, Guid testTaskId, Guid platformId, Guid versionId,
        TaskResultStatus status, string actorId, DateTime occurredOnUtc, ActionSource source)
    {
        if (id == Guid.Empty)
            throw new DomainException("An action log entry needs an id.");
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DomainException("An action log entry must record who actioned it.");

        return new ActionLogEntry(id, testTaskId, platformId, versionId, status, actorId, occurredOnUtc, source);
    }
}
