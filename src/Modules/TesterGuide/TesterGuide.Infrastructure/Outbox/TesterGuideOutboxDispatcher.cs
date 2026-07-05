using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Outbox;
using TesterGuide.Infrastructure.Persistence;
using TestPlans.Contracts;

namespace TesterGuide.Infrastructure.Outbox;

/// <summary>
/// The Tester Guide module's outbox dispatch logic (the sync saga's forward leg): maps a
/// <see cref="MainDbActionRequested"/> onto the primary system's published <see cref="ITestPlanActionLog"/>
/// contract. Delivery is idempotent in the message id (the primary keys its action-log entry by it), so a
/// redelivery is a no-op. Plugged into the shared <c>OutboxProcessor&lt;TesterGuideDbContext&gt;</c>.
/// <para>
/// On a <see cref="ActionSyncOutcome.Recorded"/> outcome it advances the originating guide action to
/// <c>Synced</c> — tracked on the same <see cref="TesterGuideDbContext"/> the processor commits, so the state
/// change lands atomically with the message's <c>ProcessedOnUtc</c>. A rejection is <b>not</b> handled here:
/// it comes back asynchronously via <c>IGuideActionReconciler</c> (the reverse leg).
/// </para>
/// </summary>
internal sealed class TesterGuideOutboxDispatcher : IOutboxDispatcher<TesterGuideDbContext>
{
    private readonly ITestPlanActionLog _actionLog;
    private readonly TesterGuideDbContext _db;

    public TesterGuideOutboxDispatcher(ITestPlanActionLog actionLog, TesterGuideDbContext db)
    {
        _actionLog = actionLog;
        _db = db;
    }

    public async Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(MainDbActionRequested):
                var requested = JsonSerializer.Deserialize<MainDbActionRequested>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                // SourceReference is the guide action id, echoed back if the primary rejects the sync.
                var outcome = await _actionLog.RecordActionAsync(
                    messageId,
                    new RecordActionInput(
                        requested.TestTaskId,
                        requested.PlatformId,
                        requested.TestPlanVersionId,
                        requested.Status,
                        requested.ActorId,
                        requested.GuideActionId),
                    cancellationToken);

                if (outcome == ActionSyncOutcome.Recorded)
                {
                    // Forward leg completed: mark our own record Synced. Not saved here — the outbox processor's
                    // SaveChanges persists it together with the message's ProcessedOnUtc. A rejection is left to
                    // the reverse leg, so the entry stays Pending until that compensation arrives.
                    var entry = await _db.ActionLog.FirstOrDefaultAsync(
                        e => e.Id == requested.GuideActionId, cancellationToken);
                    entry?.MarkSynced();
                }

                return;

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
