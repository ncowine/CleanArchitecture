using BuildingBlocks.Outbox;
using Equipment.Application.Abstractions;
using Equipment.Infrastructure.Persistence;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// Writes Equipment's integration events to the outbox table in <see cref="EquipmentDbContext"/>, through the shared
/// <see cref="OutboxWriter{TContext}"/> so the row format matches every other outbox. The row is committed by the
/// module's TransactionBehavior together with the change.
/// </summary>
internal sealed class EquipmentOutbox : IEquipmentOutbox
{
    private readonly OutboxWriter<EquipmentDbContext> _writer;

    public EquipmentOutbox(OutboxWriter<EquipmentDbContext> writer)
    {
        _writer = writer;
    }

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class => _writer.Enqueue(integrationEvent);
}
