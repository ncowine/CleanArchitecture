using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using TestPlans.Application.Abstractions;
using TestPlans.Contracts;
using TestPlans.Domain;
using TestPlans.Infrastructure.Outbox;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Contracts;

/// <summary>
/// Implements the published <see cref="ITestPlanActionLog"/> — the target for the Tester Guide's sync (the
/// saga's forward leg). It records the synced action (Source = Guide) idempotently in the originating
/// message id: the action-log entry's id <b>is</b> the message id, so a redelivery is a no-op. Because it
/// is invoked by the outbox dispatcher (outside the mediator transaction pipeline), it owns its own
/// <c>SaveChanges</c>.
/// <para>
/// When the referenced task/version/platform no longer exists, it <b>rejects</b>: no action is recorded and
/// a <see cref="MainDbActionRejected"/> compensation is enqueued in the Test Plans outbox — in the same
/// transaction — so the Guide can flag the action (the saga's reverse leg). The whole thing is idempotent in
/// the message id: an accepted message is marked by the action-log row, a rejected one by the outbox row
/// (whose id we set to the message id), so a redelivery does neither thing twice. This mirrors the shipped
/// Students <c>StudentHoldService</c> saga.
/// </para>
/// </summary>
internal sealed class TestPlanActionLog : ITestPlanActionLog
{
    private readonly TestPlansDbContext _db;
    private readonly ITaskResultStore _results;

    public TestPlanActionLog(TestPlansDbContext db, ITaskResultStore results)
    {
        _db = db;
        _results = results;
    }

    public async Task<ActionSyncOutcome> RecordActionAsync(Guid messageId, RecordActionInput input, CancellationToken cancellationToken)
    {
        // Idempotency: a redelivery reports the same outcome the first delivery reached (an action-log row
        // means it was recorded; a rejection outbox row keyed by the message id means it was rejected) so the
        // caller's own state stays consistent without doing the work twice.
        var alreadyRecorded = await _db.ActionLog.AnyAsync(entry => entry.Id == messageId, cancellationToken);
        if (alreadyRecorded)
        {
            return ActionSyncOutcome.Recorded;
        }

        var alreadyRejected = await _db.Outbox.AnyAsync(message => message.Id == messageId, cancellationToken);
        if (alreadyRejected)
        {
            return ActionSyncOutcome.Rejected;
        }

        if (!Enum.TryParse<TaskResultStatus>(input.Status, ignoreCase: true, out var status))
            throw new DomainException($"Unknown task status '{input.Status}'.");

        var taskExists = await _db.Tasks.AnyAsync(t => t.Id == input.TestTaskId, cancellationToken);
        var versionExists = await _db.Versions.AnyAsync(v => v.Id == input.TestPlanVersionId, cancellationToken);
        var platformExists = await _db.Platforms.AnyAsync(p => p.Id == input.PlatformId, cancellationToken);

        ActionSyncOutcome outcome;
        if (!taskExists || !versionExists || !platformExists)
        {
            // Reject: publish the compensation back to the Guide. The outbox row id is the originating
            // message id, which both makes the rejection idempotent and ties the legs of the saga together.
            var reason = "The synced action references a task, version, or platform that no longer exists in the primary system.";
            _db.Outbox.Add(new OutboxMessage
            {
                Id = messageId,
                Type = nameof(MainDbActionRejected),
                Content = JsonSerializer.Serialize(new MainDbActionRejected(input.SourceReference, reason)),
                OccurredOnUtc = DateTime.UtcNow,
            });
            outcome = ActionSyncOutcome.Rejected;
        }
        else
        {
            await _results.RecordAsync(
                messageId,
                input.TestTaskId,
                input.PlatformId,
                input.TestPlanVersionId,
                status,
                input.ActorId,
                ActionSource.Guide,
                DateTime.UtcNow,
                cancellationToken);
            outcome = ActionSyncOutcome.Recorded;
        }

        // One transaction: either the action is recorded, or the rejection event is enqueued.
        await _db.SaveChangesAsync(cancellationToken);
        return outcome;
    }
}
