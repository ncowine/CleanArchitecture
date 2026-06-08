using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

internal sealed class OutboxDeadLetterReader<TContext> : IDeadLetterReader where TContext : DbContext
{
    private readonly TContext _db;

    public OutboxDeadLetterReader(TContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<DeadLetterEntry>> GetAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.DeadLetteredOnUtc != null);

        // Count and page in SQL — one COUNT plus one windowed SELECT against the same filter.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(message => message.DeadLetteredOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new DeadLetterEntry(
                message.Id,
                message.Type,
                message.Attempts,
                message.Error,
                message.OccurredOnUtc,
                message.DeadLetteredOnUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<DeadLetterEntry>(items, page, pageSize, totalCount);
    }
}
