using System.Text.Json;
using BuildingBlocks.Correlation;
using BuildingBlocks.Outbox;
using Students.Application.Abstractions;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Outbox;

/// <summary>
/// Writes integration events to the Students outbox. Like the shared <c>OutboxWriter</c>, it only
/// <c>Add</c>s — never SaveChanges — so the event row commits in the same transaction as the business
/// change (TransactionBehavior). The message Type uses the event type name, matching the dispatcher.
/// </summary>
internal sealed class StudentOutbox : IStudentOutbox
{
    private readonly StudentsDbContext _db;
    private readonly ICorrelationContext _correlation;

    public StudentOutbox(StudentsDbContext db, ICorrelationContext correlation)
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
