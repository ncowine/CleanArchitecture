using Students.Domain;

namespace Students.Application.Abstractions;

public interface IStudentRepository
{
    Task AddAsync(Student student, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates it and the unit of work persists the change.</summary>
    Task<Student?> GetAsync(Guid studentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StudentHold>> GetHoldsAsync(Guid studentId, CancellationToken cancellationToken);
}
