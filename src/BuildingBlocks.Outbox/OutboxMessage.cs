namespace BuildingBlocks.Outbox;

/// <summary>
/// A persisted, not-yet-delivered integration event. Written in the same transaction as the business
/// change that produced it, then delivered asynchronously by <see cref="OutboxProcessor{TContext}"/>.
/// <see cref="ProcessedOnUtc"/> stays null until delivery succeeds; delivery is retried up to a cap,
/// after which the message is <b>dead-lettered</b> (parked, not deleted) so one poison message can't
/// spin forever or block the queue. Shared by every module — each maps it into its own DbContext.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>How many delivery attempts have been made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Set once the attempt cap is hit; the message is then parked, not retried.</summary>
    public DateTime? DeadLetteredOnUtc { get; set; }

    /// <summary>The most recent delivery error, kept for diagnostics.</summary>
    public string? Error { get; set; }
}
