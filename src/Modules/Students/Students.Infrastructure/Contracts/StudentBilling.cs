using Microsoft.EntityFrameworkCore;
using Students.Contracts;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Contracts;

/// <summary>
/// Implements the Students module's <see cref="IStudentBilling"/> against the Students database: posts a
/// library fine as a charge on the student's account (opening it on first use) and places a financial hold
/// if the charge crosses the balance limit. Idempotent in the message id — the charge entry carries it as
/// its <c>SourceReference</c>, so a redelivery is a no-op.
/// </summary>
internal sealed class StudentBilling : IStudentBilling
{
    private readonly StudentsDbContext _db;

    public StudentBilling(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task ChargeLibraryFineAsync(
        Guid messageId, Guid studentId, decimal amount, CancellationToken cancellationToken)
    {
        var account = await _db.StudentAccounts
            .FirstOrDefaultAsync(a => a.StudentId == studentId, cancellationToken);

        // Already applied? (the entry from this message exists) → no-op.
        if (account is not null && account.HasEntryFrom(messageId))
        {
            return;
        }

        if (account is null)
        {
            account = StudentAccount.Open(studentId);
            await _db.StudentAccounts.AddAsync(account, cancellationToken);
        }

        var priorBalance = account.Balance;
        account.Charge(
            amount, ChargeCategory.LibraryFine, "Library fine", DateOnly.FromDateTime(DateTime.UtcNow), messageId);

        // Same financial-hold rule as the in-process charger (shared in BillingPolicy).
        if (BillingPolicy.CrossesHoldThreshold(priorBalance, account.Balance))
        {
            _db.Holds.Add(StudentHold.Place(
                Guid.NewGuid(),
                studentId,
                $"Outstanding balance of {account.Balance:0.00} exceeds the {BillingPolicy.FinancialHoldThreshold:0.00} limit.",
                DateTime.UtcNow));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
