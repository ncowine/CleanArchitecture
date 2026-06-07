using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

internal sealed class OutboxDeadLetterReader<TContext> : IDeadLetterReader where TContext : DbContext
{
    private readonly TContext _db;

    public OutboxDeadLetterReader(TContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetAsync(CancellationToken cancellationToken) =>
        await _db.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.DeadLetteredOnUtc != null)
            .OrderByDescending(message => message.DeadLetteredOnUtc)
            .Select(message => new DeadLetterEntry(
                message.Id,
                message.Type,
                message.Attempts,
                message.Error,
                message.OccurredOnUtc,
                message.DeadLetteredOnUtc))
            .ToListAsync(cancellationToken);
}
