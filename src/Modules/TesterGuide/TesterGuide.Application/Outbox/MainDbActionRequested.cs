namespace TesterGuide.Application.Outbox;

/// <summary>
/// Integration event raised when a tester actions a task in a sync-enabled config: it asks the primary
/// system to mirror the action into its action log (the sync saga's forward leg). Enqueued in the Tester
/// Guide outbox and delivered to the primary via <c>ITestPlanActionLog</c>. <see cref="GuideActionId"/> is
/// echoed back if the primary rejects it, so the originating guide action can be flagged.
/// </summary>
public sealed record MainDbActionRequested(
    Guid GuideActionId,
    Guid TestTaskId,
    Guid PlatformId,
    Guid TestPlanVersionId,
    string Status,
    string ActorId);
