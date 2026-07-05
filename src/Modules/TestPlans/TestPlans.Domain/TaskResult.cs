namespace TestPlans.Domain;

/// <summary>
/// The <b>current</b> outcome of a task for a specific (platform, version). Exactly one row per
/// (task, platform, version) — recording a new action updates it in place, while the full history lives in
/// <see cref="ActionLogEntry"/>.
/// </summary>
public sealed class TaskResult
{
    public Guid Id { get; private set; }
    public Guid TestTaskId { get; private set; }
    public Guid PlatformId { get; private set; }
    public Guid TestPlanVersionId { get; private set; }
    public TaskResultStatus Status { get; private set; }
    public string ActorId { get; private set; } = null!;
    public DateTime ActionedOnUtc { get; private set; }

    private TaskResult() { }

    private TaskResult(
        Guid id, Guid testTaskId, Guid platformId, Guid versionId,
        TaskResultStatus status, string actorId, DateTime actionedOnUtc)
    {
        Id = id;
        TestTaskId = testTaskId;
        PlatformId = platformId;
        TestPlanVersionId = versionId;
        Status = status;
        ActorId = actorId;
        ActionedOnUtc = actionedOnUtc;
    }

    public static TaskResult Create(
        Guid testTaskId, Guid platformId, Guid versionId,
        TaskResultStatus status, string actorId, DateTime actionedOnUtc)
    {
        if (testTaskId == Guid.Empty)
            throw new DomainException("A result must reference a task.");
        if (platformId == Guid.Empty)
            throw new DomainException("A result must reference a platform.");
        if (versionId == Guid.Empty)
            throw new DomainException("A result must reference a version.");
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DomainException("A result must record who actioned it.");

        return new TaskResult(Guid.NewGuid(), testTaskId, platformId, versionId, status, actorId, actionedOnUtc);
    }

    public void Update(TaskResultStatus status, string actorId, DateTime actionedOnUtc)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            throw new DomainException("A result must record who actioned it.");

        Status = status;
        ActorId = actorId;
        ActionedOnUtc = actionedOnUtc;
    }
}
