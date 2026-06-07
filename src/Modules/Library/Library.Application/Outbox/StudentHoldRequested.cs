namespace Library.Application.Outbox;

/// <summary>
/// Integration event raised by the Library module when a student's fines cross the hold limit. It is
/// enqueued in the outbox and later delivered to the main Students module, which places the hold.
/// Internal to Library — the dispatcher maps it onto the Students module's published contract.
/// </summary>
public sealed record StudentHoldRequested(Guid StudentId, string Reason);
