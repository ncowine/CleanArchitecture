using BuildingBlocks.Pagination;
using Students.Application.Students;
using Students.Domain;

namespace Students.Application.Abstractions;

/// <summary>
/// Read-side projections for the Students module's own endpoints. Separate from <c>IStudentRepository</c>
/// (the write side) — each method returns a purpose-built response shape rather than a domain entity, so
/// the SQL fetches exactly what that shape needs.
/// </summary>
public interface IStudentReadService
{
    Task<GetStudent.Response?> GetSummaryAsync(Guid studentId, CancellationToken cancellationToken);

    Task<GetStudentDetail.Response?> GetDetailAsync(Guid studentId, CancellationToken cancellationToken);

    Task<PagedResult<GetStudent.Response>> SearchAsync(
        int page, int pageSize, StudentStatus? status, CancellationToken cancellationToken);
}
