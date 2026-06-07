using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Writes integration events to a context's outbox table. It only <c>Add</c>s to the context — it
/// never calls SaveChanges — so the row is committed by the same unit of work (TransactionBehavior)
/// that commits the business change. That shared transaction is what makes "business change + event"
/// atomic.
/// </summary>
internal sealed class OutboxWriter<TContext> : IOutbox where TContext : DbContext
{
    private readonly TContext _db;

    public OutboxWriter(TContext db)
    {
        _db = db;
    }

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(TEvent).Name,
            Content = JsonSerializer.Serialize(integrationEvent),
            OccurredOnUtc = DateTime.UtcNow,
        });
    }
}
