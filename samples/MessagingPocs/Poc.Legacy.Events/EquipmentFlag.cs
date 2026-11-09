namespace Poc.Legacy.Events;

/// <summary>The payload of <see cref="EquipmentFlagged"/>.</summary>
public class EquipmentFlag
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}
