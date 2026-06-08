namespace Students.Application.Outbox;

/// <summary>
/// Integration event published when a student is withdrawn. Delivered to the Library module (the forward
/// leg of the withdrawal saga), where it returns the student's loans and cancels their reservations.
/// </summary>
public sealed record StudentWithdrawn(Guid StudentId);
