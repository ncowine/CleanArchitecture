using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Billing;

public sealed class AccountCharger : IAccountCharger
{
    private readonly IStudentAccountRepository _accounts;
    private readonly IStudentRepository _students;

    public AccountCharger(IStudentAccountRepository accounts, IStudentRepository students)
    {
        _accounts = accounts;
        _students = students;
    }

    public async Task<decimal> ChargeAsync(
        Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByStudentAsync(studentId, cancellationToken);
        if (account is null)
        {
            account = StudentAccount.Open(studentId);
            await _accounts.AddAsync(account, cancellationToken);
        }

        var priorBalance = account.Balance;
        account.Charge(amount, category, description, DateOnly.FromDateTime(DateTime.UtcNow));

        // Place a financial hold only on the transition over the threshold (so repeated charges over the
        // limit don't pile up holds).
        if (BillingPolicy.CrossesHoldThreshold(priorBalance, account.Balance))
        {
            var hold = StudentHold.Place(
                Guid.NewGuid(),
                studentId,
                $"Outstanding balance of {account.Balance:0.00} exceeds the {BillingPolicy.FinancialHoldThreshold:0.00} limit.",
                DateTime.UtcNow);

            await _students.AddHoldAsync(hold, cancellationToken);
        }

        return account.Balance;
    }
}
