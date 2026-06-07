using Library.Contracts;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Contracts;

/// <summary>
/// Implements the Library module's <see cref="IFineWaiver"/> compensation against the Library DB. It
/// is the consumer end of the saga's reverse leg: the Students dispatcher calls it after rejecting a
/// hold. Naturally idempotent — a redelivery finds no outstanding fines left to waive.
/// </summary>
internal sealed class FineWaiver : IFineWaiver
{
    private readonly LibraryDbContext _db;

    public FineWaiver(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task WaiveStudentFinesAsync(Guid studentId, string reason, CancellationToken cancellationToken)
    {
        var finedLoans = await _db.Loans
            .Where(loan => loan.StudentId == studentId && loan.FineAmount > 0m)
            .ToListAsync(cancellationToken);

        if (finedLoans.Count == 0)
        {
            return;
        }

        foreach (var loan in finedLoans)
        {
            loan.WaiveFine();
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
