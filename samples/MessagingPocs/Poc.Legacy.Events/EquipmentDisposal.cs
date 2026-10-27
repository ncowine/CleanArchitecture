namespace Poc.Legacy.Events;

/// <summary>The payload of <see cref="EquipmentDisposed"/>. Its properties match the API's copy exactly.</summary>
public class EquipmentDisposal
{
    public Guid EquipmentId { get; set; }

    public string AssetTag { get; set; } = "";

    public string DisposedBy { get; set; } = "";
}
