using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

internal sealed class OutboxReplayer<TContext> : IOutboxReplayer where TContext : DbContext
{
    private readonly TContext _db;

    public OutboxReplayer(TContext db)
    {
        _db = db;
    }

    public async Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _db.Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.DeadLetteredOnUtc != null, cancellationToken);

        if (message is null)
        {
            return false;
        }

        // Reset to a fresh, undelivered state. ProcessedOnUtc is already null, so clearing the
        // dead-letter flag and attempt count makes it eligible for the dispatcher's next poll. This is
        // a standalone admin operation, so it commits itself.
        message.DeadLetteredOnUtc = null;
        message.Attempts = 0;
        message.Error = null;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
