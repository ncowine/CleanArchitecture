using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Application.Billing;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Reads;

internal sealed class AccountReadService : IAccountReadService
{
    private readonly StudentsDbContext _db;

    public AccountReadService(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task<GetStudentAccount.Response> GetAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Balance is stored on the account, not aggregated — read it directly.
        var balance = await _db.StudentAccounts
            .AsNoTracking()
            .Where(account => account.StudentId == studentId)
            .Select(account => (decimal?)account.Balance)
            .FirstOrDefaultAsync(cancellationToken);

        if (balance is null)
        {
            // No account yet (never charged) → zero balance, empty statement.
            return new GetStudentAccount.Response(
                studentId, 0m,
                new PagedResult<GetStudentAccount.EntryDto>(Array.Empty<GetStudentAccount.EntryDto>(), page, pageSize, 0));
        }

        var entries = _db.StudentAccounts
            .AsNoTracking()
            .Where(account => account.StudentId == studentId)
            .SelectMany(account => account.Entries);

        var totalCount = await entries.CountAsync(cancellationToken);

        var rows = await entries
            .OrderByDescending(entry => entry.OccurredOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new { entry.Id, entry.Kind, entry.Category, entry.Amount, entry.Description, entry.OccurredOn })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new GetStudentAccount.EntryDto(
                r.Id, r.Kind.ToString(), r.Category?.ToString(), r.Amount, r.Description, r.OccurredOn))
            .ToList();

        return new GetStudentAccount.Response(
            studentId, balance.Value, new PagedResult<GetStudentAccount.EntryDto>(items, page, pageSize, totalCount));
    }
}
