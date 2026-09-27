using Equipment.Application.Abstractions;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// The default when the host hasn't turned on messaging (no <c>Messaging</c> configuration): records nothing, because
/// no other application is listening and no processor would ever deliver the rows. Replaced by
/// <see cref="EquipmentOutbox"/> in <c>AddEquipmentIntegrationEvents</c>.
/// </summary>
/// <remarks>
/// This keeps a developer without a RabbitMQ broker exactly where they were: same behaviour, no outbox rows piling up.
/// Real-time (SignalR) notifications are unaffected; they don't go through the outbox.
/// </remarks>
internal sealed class NoBrokerEquipmentOutbox : IEquipmentOutbox
{
    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class
    {
    }
}
