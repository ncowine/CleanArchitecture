using BuildingBlocks.Pagination;
using Students.Domain;

namespace Students.Application.Abstractions;

public interface IStudentRepository
{
    Task AddAsync(Student student, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates it and the unit of work persists the change.</summary>
    Task<Student?> GetAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>A page of the student's holds, newest first, with the total count for navigation.</summary>
    Task<PagedResult<StudentHold>> GetHoldsAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Stages a new hold (e.g. a financial hold). The unit of work commits it.</summary>
    Task AddHoldAsync(StudentHold hold, CancellationToken cancellationToken);
}
