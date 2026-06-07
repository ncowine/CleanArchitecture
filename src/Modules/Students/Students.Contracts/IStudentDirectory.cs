namespace Students.Contracts;

/// <summary>
/// Read-only snapshot of a student, published by the Students module for other modules to consume.
/// Carries only the fields outside modules are allowed to depend on — never the Student aggregate.
/// </summary>
public sealed record StudentSummary(Guid Id, string FullName, string Email, string Status);

/// <summary>
/// The Students module's published contract. Lets another module look up a student by id without
/// referencing Students' domain, application, or <c>DbContext</c>. This is the only sanctioned way
/// to read student reference data across a module boundary — and, in turn, across the Students
/// database boundary. The implementation lives in Students.Infrastructure and owns the DB access.
/// </summary>
public interface IStudentDirectory
{
    Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken);
}
