using Messaging;

namespace Poc.Api.NoOutbox;

/// <summary>
/// The legacy event Poc.Legacy.Events.EquipmentFlagged, whose payload is the class EquipmentFlag { Id, Name }.
/// </summary>
[Message("Poc.Legacy.Events.EquipmentFlagged")]
public sealed class EquipmentFlagged
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}
