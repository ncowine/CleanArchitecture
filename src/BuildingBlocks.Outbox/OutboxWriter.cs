using System.Text.Json;
using BuildingBlocks.Correlation;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Writes integration events to a context's outbox table. It only <c>Add</c>s to the context — it
/// never calls SaveChanges — so the row is committed by the same unit of work (TransactionBehavior)
/// that commits the business change. That shared transaction is what makes "business change + event"
/// atomic.
/// <para>
/// Public so a second module can wrap it behind its OWN outbox interface (e.g. <c>IEquipmentOutbox</c>) instead
/// of registering another plain <see cref="IOutbox"/>, which would collide with the first module's. Reusing it,
/// rather than copying it, keeps the row format (the short type name, the JSON) the same everywhere, which the
/// outbox dispatchers rely on.
/// </para>
/// </summary>
public sealed class OutboxWriter<TContext> : IOutbox where TContext : DbContext
{
    private readonly TContext _db;
    private readonly ICorrelationContext _correlation;

    public OutboxWriter(TContext db, ICorrelationContext correlation)
    {
        _db = db;
        _correlation = correlation;
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
            CorrelationId = _correlation.CorrelationId,
        });
    }
}
