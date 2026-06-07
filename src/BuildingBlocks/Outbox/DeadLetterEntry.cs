namespace BuildingBlocks.Outbox;

/// <summary>A read-only view of an outbox message that exhausted its delivery attempts and was parked.</summary>
public sealed record DeadLetterEntry(
    Guid Id,
    string Type,
    int Attempts,
    string? Error,
    DateTime OccurredOnUtc,
    DateTime? DeadLetteredOnUtc);
