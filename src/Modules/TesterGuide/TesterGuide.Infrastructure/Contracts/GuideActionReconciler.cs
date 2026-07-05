using Microsoft.EntityFrameworkCore;
using TesterGuide.Contracts;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Contracts;

/// <summary>
/// Implements the published <see cref="IGuideActionReconciler"/> — the sync saga's reverse leg landing in
/// the Tester Guide database. Flags the originating guide action as rejected with the primary's reason.
/// Invoked by the primary system's outbox dispatcher (outside the mediator pipeline), so it owns its own
/// <c>SaveChanges</c>. Idempotent: re-flagging an already-rejected action is a no-op.
/// </summary>
internal sealed class GuideActionReconciler : IGuideActionReconciler
{
    private readonly TesterGuideDbContext _db;

    public GuideActionReconciler(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task MarkSyncRejectedAsync(
        Guid messageId, Guid guideActionId, string reason, CancellationToken cancellationToken)
    {
        var entry = await _db.ActionLog.FirstOrDefaultAsync(e => e.Id == guideActionId, cancellationToken);
        if (entry is null || entry.SyncState == Domain.SyncState.Rejected)
        {
            return;
        }

        entry.MarkSyncRejected(reason);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
