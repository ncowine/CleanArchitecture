using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: nullable values, some of them null: nulls are written, not left out.</summary>
[Message("Poc.Legacy.Events.Shapes.WarrantyChecked")]
public sealed class WarrantyChecked
{
    public DateTime? ExpiresUtc { get; set; }

    public int? DaysLeft { get; set; }

    public string? Note { get; set; }

    public override string ToString() => FormattableString.Invariant($"expires {(ExpiresUtc is null ? "null" : ExpiresUtc.Value.ToString("O"))}, days left {(DaysLeft is null ? "null" : DaysLeft.ToString())}, note {Note ?? "null"}");
}
