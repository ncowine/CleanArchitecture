namespace Library.Contracts;

/// <summary>
/// The Library module's published reaction to a student withdrawal. The Students module calls it (via its
/// outbox dispatcher) when a student is withdrawn; the Library then returns the student's active loans and
/// cancels their reservations. Implementations MUST be idempotent — the event is delivered at least once.
/// </summary>
public interface ILibraryWithdrawalService
{
    Task OnStudentWithdrawnAsync(Guid studentId, CancellationToken cancellationToken);
}
