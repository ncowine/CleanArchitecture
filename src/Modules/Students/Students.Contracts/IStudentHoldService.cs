namespace Students.Contracts;

/// <summary>
/// The Students module's published write contract for placing holds. Other modules ask for a hold
/// through this — they never touch the Students <c>DbContext</c>. Implementations MUST be idempotent
/// in <paramref name="messageId"/>: the outbox delivers at least once, so the same hold request can
/// arrive more than once and must only ever take effect once.
/// </summary>
public interface IStudentHoldService
{
    Task PlaceHoldAsync(Guid messageId, Guid studentId, string reason, CancellationToken cancellationToken);
}
