using Messaging;

namespace Poc.Api.NoOutbox;

/// <summary>
/// A legacy app recorded that a piece of equipment was disposed of. Received from RabbitMQ, never sent. The wire name is
/// the full name of the sender's Prism event, and the properties match the payload it carries (EquipmentDisposal).
/// </summary>
[Message("Poc.Legacy.Events.EquipmentDisposed")]
public sealed class EquipmentDisposed
{
    public Guid EquipmentId { get; set; }

    public string AssetTag { get; set; } = "";

    public string DisposedBy { get; set; } = "";
}
