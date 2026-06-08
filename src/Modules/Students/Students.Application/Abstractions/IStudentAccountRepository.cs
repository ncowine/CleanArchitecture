using Students.Domain;

namespace Students.Application.Abstractions;

public interface IStudentAccountRepository
{
    Task AddAsync(StudentAccount account, CancellationToken cancellationToken);

    /// <summary>Tracked load (with entries) — the caller posts to the ledger and the unit of work persists it.</summary>
    Task<StudentAccount?> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken);
}
