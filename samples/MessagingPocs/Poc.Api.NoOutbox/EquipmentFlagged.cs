using System.Text.Json.Serialization;
using Messaging;

namespace Poc.Api.NoOutbox;

/// <summary>
/// The legacy event Poc.Legacy.Events.EquipmentFlagged, whose payload is the tuple (Guid id, string name). A tuple's names
/// don't reach the wire, so this class maps its items by position.
/// </summary>
/// <remarks>
/// ❌ DON'T receive into a tuple or drop JsonPropertyName: nothing throws, the values are just Guid.Empty and empty.
/// </remarks>
[Message("Poc.Legacy.Events.EquipmentFlagged")]
public sealed class EquipmentFlagged
{
    [JsonPropertyName("Item1")]
    public Guid Id { get; set; }

    [JsonPropertyName("Item2")]
    public string Name { get; set; } = "";
}
