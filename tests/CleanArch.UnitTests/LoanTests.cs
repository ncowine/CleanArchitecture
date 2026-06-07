using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class LoanTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly DateOnly Due = new(2026, 3, 1);

    [Fact]
    public void Borrow_with_valid_input_creates_loan()
    {
        var studentId = Guid.NewGuid();
        var loan = Loan.Borrow(studentId, "  SICP  ", Today, Due);

        Assert.Equal(studentId, loan.StudentId);
        Assert.Equal("SICP", loan.BookTitle); // trimmed
        Assert.Equal(0m, loan.FineAmount);
        Assert.Null(loan.ReturnedOn);
    }

    [Fact]
    public void Borrow_with_empty_student_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.Empty, "X", Today, Due));

    [Fact]
    public void Borrow_with_blank_title_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.NewGuid(), "   ", Today, Due));

    [Fact]
    public void Borrow_with_due_not_after_borrow_throws() =>
        Assert.Throws<DomainException>(() => Loan.Borrow(Guid.NewGuid(), "X", Today, Today));

    [Fact]
    public void AssessFine_accumulates()
    {
        var loan = Loan.Borrow(Guid.NewGuid(), "X", Today, Due);
        loan.AssessFine(5m);
        loan.AssessFine(2.5m);
        Assert.Equal(7.5m, loan.FineAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AssessFine_with_non_positive_throws(decimal amount)
    {
        var loan = Loan.Borrow(Guid.NewGuid(), "X", Today, Due);
        Assert.Throws<DomainException>(() => loan.AssessFine(amount));
    }

    [Fact]
    public void WaiveFine_sets_zero()
    {
        var loan = Loan.Borrow(Guid.NewGuid(), "X", Today, Due);
        loan.AssessFine(30m);
        loan.WaiveFine();
        Assert.Equal(0m, loan.FineAmount);
    }
}
