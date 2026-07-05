using TesterGuide.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class GuideActionLogEntryTests
{
    private static GuideActionLogEntry Synced()
    {
        var entry = Record(syncRequested: true);
        entry.MarkSynced();
        return entry;
    }

    private static GuideActionLogEntry Record(bool syncRequested) =>
        GuideActionLogEntry.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ActionStatus.Pass, "ada", DateTime.UtcNow, syncRequested);

    [Fact]
    public void Sync_requested_starts_pending()
    {
        var entry = Record(syncRequested: true);

        Assert.Equal(SyncState.Pending, entry.SyncState);
    }

    [Fact]
    public void Sync_not_requested_starts_not_synced()
    {
        var entry = Record(syncRequested: false);

        Assert.Equal(SyncState.NotSynced, entry.SyncState);
    }

    [Fact]
    public void MarkSynced_advances_a_pending_action_to_synced()
    {
        var entry = Record(syncRequested: true);

        entry.MarkSynced();

        Assert.Equal(SyncState.Synced, entry.SyncState);
        Assert.Null(entry.SyncError);
    }

    [Fact]
    public void MarkSyncRejected_flags_a_pending_action_with_the_reason()
    {
        var entry = Record(syncRequested: true);

        entry.MarkSyncRejected("gone");

        Assert.Equal(SyncState.Rejected, entry.SyncState);
        Assert.Equal("gone", entry.SyncError);
    }

    [Fact]
    public void MarkSynced_never_reached_when_sync_was_not_requested()
    {
        // NotSynced is terminal for a sync-disabled action: a stray forward-leg mark must not touch it.
        var entry = Record(syncRequested: false);

        entry.MarkSynced();

        Assert.Equal(SyncState.NotSynced, entry.SyncState);
    }

    [Fact]
    public void MarkSynced_is_idempotent_on_an_already_synced_action()
    {
        var entry = Synced();

        entry.MarkSynced();

        Assert.Equal(SyncState.Synced, entry.SyncState);
    }

    [Fact]
    public void A_rejected_action_is_not_resurrected_by_a_redelivered_sync()
    {
        // At-least-once redelivery could call MarkSynced after a rejection already landed; the terminal
        // Rejected state must win so the reverse leg's compensation is never silently undone.
        var entry = Record(syncRequested: true);
        entry.MarkSyncRejected("gone");

        entry.MarkSynced();

        Assert.Equal(SyncState.Rejected, entry.SyncState);
        Assert.Equal("gone", entry.SyncError);
    }
}
