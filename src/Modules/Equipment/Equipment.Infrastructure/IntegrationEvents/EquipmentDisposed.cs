using Messaging;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// A legacy desktop app recorded that a piece of equipment was disposed of. Received from RabbitMQ, never sent.
/// </summary>
/// <remarks>
/// The sender is a legacy Prism app (samples/MessagingPocs/Poc.Net472): its event is
/// <c>Poc.Legacy.Events.EquipmentDisposed : PubSubEvent&lt;EquipmentDisposal&gt;</c>, so the wire name is that class's
/// full name and the body is the payload. This class mirrors the payload's properties; Newtonsoft (sender) and
/// System.Text.Json (here) read plain get/set properties the same way.
/// </remarks>
[Message("Poc.Legacy.Events.EquipmentDisposed")]
public sealed class EquipmentDisposed
{
    public Guid EquipmentId { get; set; }

    public string AssetTag { get; set; } = "";

    public string DisposedBy { get; set; } = "";
}
