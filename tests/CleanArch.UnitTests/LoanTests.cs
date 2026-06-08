using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class LoanTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly DateOnly Due = new(2026, 1, 22);

    private static Loan Borrow() => Loan.Borrow(Guid.NewGuid(), Guid.NewGuid(), Today, Due);

    [Fact]
    public void Borrow_with_valid_input_creates_loan()
    {
        var studentId = Guid.NewGuid();
        var copyId = Guid.NewGuid();

        var loan = Loan.Borrow(studentId, copyId, Today, Due);

        Assert.Equal(studentId, loan.StudentId);
        Assert.Equal(copyId, loan.CopyId);
        Assert.Equal(0m, loan.FineAmount);
        Assert.False(loan.IsReturned);
    }

    [Fact]
    public void Borrow_with_empty_student_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.Empty, Guid.NewGuid(), Today, Due));

    [Fact]
    public void Borrow_with_empty_copy_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.NewGuid(), Guid.Empty, Today, Due));

    [Fact]
    public void Borrow_with_due_not_after_borrow_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.NewGuid(), Guid.NewGuid(), Today, Today));

    [Fact]
    public void Returning_on_time_assesses_no_fine()
    {
        var loan = Borrow();

        var fine = loan.Return(Due, 0.25m);

        Assert.Equal(0m, fine);
        Assert.True(loan.IsReturned);
        Assert.Equal(0m, loan.FineAmount);
    }

    [Fact]
    public void Returning_late_accrues_a_per_day_fine()
    {
        var loan = Borrow();

        var fine = loan.Return(Due.AddDays(4), 0.25m); // four days late

        Assert.Equal(1.0m, fine);
        Assert.Equal(1.0m, loan.FineAmount);
    }

    [Fact]
    public void Returning_an_already_returned_loan_throws()
    {
        var loan = Borrow();
        loan.Return(Due, 0.25m);

        Assert.Throws<DomainException>(() => loan.Return(Due, 0.25m));
    }

    [Fact]
    public void Renew_extends_the_due_date_and_counts()
    {
        var loan = Borrow();

        loan.Renew(21, maxRenewals: 2);

        Assert.Equal(Due.AddDays(21), loan.DueOn);
        Assert.Equal(1, loan.RenewalCount);
    }

    [Fact]
    public void Renewing_past_the_limit_throws()
    {
        var loan = Borrow();
        loan.Renew(21, maxRenewals: 2);
        loan.Renew(21, maxRenewals: 2);

        Assert.Throws<DomainException>(() => loan.Renew(21, maxRenewals: 2));
    }

    [Fact]
    public void Renewing_a_returned_loan_throws()
    {
        var loan = Borrow();
        loan.Return(Due, 0.25m);

        Assert.Throws<DomainException>(() => loan.Renew(21, maxRenewals: 2));
    }

    [Fact]
    public void AssessFine_accumulates()
    {
        var loan = Borrow();
        loan.AssessFine(5m);
        loan.AssessFine(2.5m);
        Assert.Equal(7.5m, loan.FineAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AssessFine_with_non_positive_throws(decimal amount)
    {
        var loan = Borrow();
        Assert.Throws<DomainException>(() => loan.AssessFine(amount));
    }

    [Fact]
    public void WaiveFine_sets_zero()
    {
        var loan = Borrow();
        loan.AssessFine(30m);
        loan.WaiveFine();
        Assert.Equal(0m, loan.FineAmount);
    }
}
