namespace Library.Contracts;

/// <summary>
/// The Library module's published compensation contract. The Students module calls it (via its own
/// outbox dispatcher) when it rejects a hold the Library asked for — the Library then writes off the
/// fines that triggered the request. Implementations MUST be idempotent: the compensation event is
/// delivered at least once.
/// </summary>
public interface IFineWaiver
{
    Task WaiveStudentFinesAsync(Guid studentId, string reason, CancellationToken cancellationToken);
}
