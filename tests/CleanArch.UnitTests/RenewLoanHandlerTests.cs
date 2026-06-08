using Library.Application.Loans;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class RenewLoanHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    [Fact]
    public async Task Renews_and_extends_the_due_date()
    {
        var copyId = Guid.NewGuid();
        var loan = Loan.Borrow(Guid.NewGuid(), copyId, Today, Today.AddDays(21));
        var loans = new FakeLoanRepository();
        loans.Seed(loan);
        var handler = new RenewLoan.Handler(loans);

        var result = await handler.Handle(new RenewLoan.Command(copyId), default);

        Assert.Equal(1, result.RenewalCount);
        Assert.Equal(Today.AddDays(21 + CirculationPolicy.LoanPeriodDays), result.DueOn);
    }

    [Fact]
    public async Task No_active_loan_for_the_copy_throws()
    {
        var handler = new RenewLoan.Handler(new FakeLoanRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new RenewLoan.Command(Guid.NewGuid()), default));
    }
}
