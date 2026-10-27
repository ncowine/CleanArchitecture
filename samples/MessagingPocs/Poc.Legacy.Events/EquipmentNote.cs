namespace Poc.Legacy.Events;

/// <summary>The payload of <see cref="EquipmentNoteSent"/>.</summary>
public class EquipmentNote
{
    public Guid EquipmentId { get; set; }

    public string AssetTag { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Which app sent it, e.g. "net472 (pid 1234)".</summary>
    public string From { get; set; } = "";
}
