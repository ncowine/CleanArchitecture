using Library.Application.Loans;
using Library.Domain;
using Students.Contracts;
using Xunit;

namespace CleanArch.UnitTests;

public class BorrowBookHandlerTests
{
    private static readonly DateOnly Acquired = new(2026, 1, 1);

    private static BookCopy AvailableCopy() =>
        BookCopy.Create(Guid.NewGuid(), "BC-1", CopyCondition.Good, Acquired);

    private static BorrowBook.Handler HandlerFor(
        FakeLoanRepository loans, FakeBookCopyRepository copies, StudentSummary? student) =>
        new(loans, copies, new FakeStudentDirectory(student));

    [Fact]
    public async Task Borrows_an_available_copy_for_an_active_student()
    {
        var studentId = Guid.NewGuid();
        var copy = AvailableCopy();
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var loans = new FakeLoanRepository();
        var handler = HandlerFor(loans, copies, new StudentSummary(studentId, "Ada", "a@b.com", "Active"));

        var id = await handler.Handle(new BorrowBook.Command(studentId, copy.Id), default);

        var added = Assert.Single(loans.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal(copy.Id, added.CopyId);
        Assert.Equal(CopyStatus.OnLoan, copy.Status);
    }

    [Fact]
    public async Task Missing_student_throws_and_writes_no_loan()
    {
        var copy = AvailableCopy();
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var loans = new FakeLoanRepository();
        var handler = HandlerFor(loans, copies, student: null);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new BorrowBook.Command(Guid.NewGuid(), copy.Id), default));

        Assert.Empty(loans.Added);
    }

    [Fact]
    public async Task An_unavailable_copy_cannot_be_borrowed()
    {
        var studentId = Guid.NewGuid();
        var copy = AvailableCopy();
        copy.MarkOnLoan(); // already out
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var handler = HandlerFor(new FakeLoanRepository(), copies, new StudentSummary(studentId, "Ada", "a@b.com", "Active"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new BorrowBook.Command(studentId, copy.Id), default));
    }

    [Fact]
    public async Task A_withdrawn_student_cannot_borrow()
    {
        var studentId = Guid.NewGuid();
        var copy = AvailableCopy();
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var handler = HandlerFor(new FakeLoanRepository(), copies, new StudentSummary(studentId, "Ada", "a@b.com", "Withdrawn"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new BorrowBook.Command(studentId, copy.Id), default));
    }

    [Fact]
    public async Task Borrowing_past_the_active_loan_limit_throws()
    {
        var studentId = Guid.NewGuid();
        var copy = AvailableCopy();
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var loans = new FakeLoanRepository();
        for (var i = 0; i < CirculationPolicy.BorrowerLoanLimit; i++)
        {
            loans.Seed(Loan.Borrow(studentId, Guid.NewGuid(), Acquired, Acquired.AddDays(21)));
        }
        var handler = HandlerFor(loans, copies, new StudentSummary(studentId, "Ada", "a@b.com", "Active"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new BorrowBook.Command(studentId, copy.Id), default));
    }
}
