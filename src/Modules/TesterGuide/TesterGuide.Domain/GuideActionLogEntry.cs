namespace TesterGuide.Domain;

/// <summary>
/// A tester's action against a task within a config — the Tester Guide's own action log. When the config
/// has sync enabled, the action is also mirrored into the primary system's action log via the outbox; its
/// <see cref="SyncState"/> tracks that outcome (and a rejection reason if the primary refused it).
/// </summary>
public sealed class GuideActionLogEntry
{
    public Guid Id { get; private set; }
    public Guid GuideConfigId { get; private set; }
    public Guid TestTaskId { get; private set; }
    public Guid PlatformId { get; private set; }
    public Guid TestPlanVersionId { get; private set; }
    public ActionStatus Status { get; private set; }
    public string UserId { get; private set; } = null!;
    public DateTime OccurredOnUtc { get; private set; }
    public SyncState SyncState { get; private set; }
    public string? SyncError { get; private set; }

    private GuideActionLogEntry() { }

    private GuideActionLogEntry(
        Guid id, Guid guideConfigId, Guid testTaskId, Guid platformId, Guid versionId,
        ActionStatus status, string userId, DateTime occurredOnUtc, SyncState syncState)
    {
        Id = id;
        GuideConfigId = guideConfigId;
        TestTaskId = testTaskId;
        PlatformId = platformId;
        TestPlanVersionId = versionId;
        Status = status;
        UserId = userId;
        OccurredOnUtc = occurredOnUtc;
        SyncState = syncState;
    }

    public static GuideActionLogEntry Record(
        Guid guideConfigId, Guid testTaskId, Guid platformId, Guid versionId,
        ActionStatus status, string userId, DateTime occurredOnUtc, bool syncRequested)
    {
        if (guideConfigId == Guid.Empty)
            throw new DomainException("An action must reference a config.");
        if (testTaskId == Guid.Empty)
            throw new DomainException("An action must reference a task.");
        if (platformId == Guid.Empty)
            throw new DomainException("An action must reference a platform.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("An action must record who performed it.");

        var syncState = syncRequested ? SyncState.Pending : SyncState.NotSynced;
        return new GuideActionLogEntry(
            Guid.NewGuid(), guideConfigId, testTaskId, platformId, versionId, status, userId, occurredOnUtc, syncState);
    }

    /// <summary>The primary system recorded this synced action — the forward leg completed successfully.</summary>
    public void MarkSynced()
    {
        // Only a pending sync advances to Synced. A terminal state wins, so an at-least-once redelivery of an
        // already-resolved action can never resurrect or flip it.
        if (SyncState != SyncState.Pending)
        {
            return;
        }

        SyncState = SyncState.Synced;
    }

    /// <summary>Compensation from the primary system: it refused to record this synced action.</summary>
    public void MarkSyncRejected(string reason)
    {
        SyncState = SyncState.Rejected;
        SyncError = reason;
    }
}
