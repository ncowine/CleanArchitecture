using Messaging;

namespace Poc.Contracts;

/// <summary>
/// "A piece of equipment changed status." Sent by the server, received by the RabbitMQ POCs.
/// </summary>
/// <remarks>
/// ✅ DO give every message an explicit wire name with <see cref="MessageAttribute"/>. Receivers identify a message by
///    this string, not by its .NET type, so the class can be renamed or moved without breaking anyone.
/// ✅ DO keep it to plain get/set properties and a parameterless constructor: the server writes JSON with
///    System.Text.Json and legacy apps read it with Newtonsoft, and simple shapes read the same in both.
/// ❌ DON'T change the wire name, or rename or retype a property, once anything receives it. Add new properties instead.
/// ❌ DON'T put whole aggregates in a message. Ids and the facts that changed; receivers ask the API for more.
/// </remarks>
[Message("Poc.EquipmentStatusChanged")]
public sealed class EquipmentStatusChanged
{
    public Guid EquipmentId { get; set; }

    public string AssetTag { get; set; } = "";

    public string Status { get; set; } = "";

    public DateTime ChangedAtUtc { get; set; }
}
