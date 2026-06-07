namespace Students.Infrastructure.Outbox;

/// <summary>
/// Integration event published by the Students module when it rejects a hold the Library requested.
/// Delivered back to the Library module (the saga's reverse leg), where it triggers the compensating
/// fine waiver. Internal to Students — the dispatcher maps it onto the Library's published contract.
/// </summary>
public sealed record StudentHoldRejected(Guid StudentId, string Reason);
