namespace Students.Contracts;

/// <summary>
/// The Students module's published billing contract. Lets another module post a charge to a student's
/// account by id, without referencing Students' domain or <c>DbContext</c>. Implementations MUST be
/// idempotent in the <c>messageId</c> — the event is delivered at least once.
/// </summary>
public interface IStudentBilling
{
    Task ChargeLibraryFineAsync(Guid messageId, Guid studentId, decimal amount, CancellationToken cancellationToken);
}
