using System.Text.Json;
using BuildingBlocks.Correlation;
using BuildingBlocks.Outbox;
using TesterGuide.Application.Abstractions;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Outbox;

/// <summary>
/// Writes integration events to the Tester Guide outbox. Like the shared <c>OutboxWriter</c>, it only
/// <c>Add</c>s — never SaveChanges — so the event row commits in the same transaction as the business
/// change (TransactionBehavior). The message Type uses the event type name, matching the dispatcher.
/// </summary>
internal sealed class TesterGuideOutbox : ITesterGuideOutbox
{
    private readonly TesterGuideDbContext _db;
    private readonly ICorrelationContext _correlation;

    public TesterGuideOutbox(TesterGuideDbContext db, ICorrelationContext correlation)
    {
        _db = db;
        _correlation = correlation;
    }

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(TEvent).Name,
            Content = JsonSerializer.Serialize(integrationEvent),
            OccurredOnUtc = DateTime.UtcNow,
            CorrelationId = _correlation.CorrelationId,
        });
    }
}
