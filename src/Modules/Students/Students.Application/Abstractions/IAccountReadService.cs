using Students.Application.Billing;

namespace Students.Application.Abstractions;

/// <summary>Read-side projection of a student's account: balance plus a paged statement of entries.</summary>
public interface IAccountReadService
{
    Task<GetStudentAccount.Response> GetAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken);
}
