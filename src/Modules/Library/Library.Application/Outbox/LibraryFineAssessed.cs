namespace Library.Application.Outbox;

/// <summary>
/// Integration event raised whenever a library fine is assessed. Delivered to the main Students module,
/// which posts the fine as a charge on the student's account. Internal to Library — the dispatcher maps it
/// onto the Students module's published billing contract.
/// </summary>
public sealed record LibraryFineAssessed(Guid StudentId, decimal Amount);
