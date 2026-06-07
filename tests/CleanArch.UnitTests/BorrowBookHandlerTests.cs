using Library.Application.Loans;
using Library.Domain;
using Students.Contracts;
using Xunit;

namespace CleanArch.UnitTests;

public class BorrowBookHandlerTests
{
    private static readonly DateOnly Due = new(2026, 9, 1);

    [Fact]
    public async Task Existing_student_creates_a_loan()
    {
        var studentId = Guid.NewGuid();
        var directory = new FakeStudentDirectory(new StudentSummary(studentId, "Ada Lovelace", "a@b.com", "Active"));
        var loans = new FakeLoanRepository();
        var handler = new BorrowBook.Handler(loans, directory);

        var id = await handler.Handle(new BorrowBook.Command(studentId, "SICP", Due), default);

        var added = Assert.Single(loans.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal(studentId, added.StudentId);
        Assert.Equal("SICP", added.BookTitle);
    }

    [Fact]
    public async Task Missing_student_throws_and_writes_no_loan()
    {
        var loans = new FakeLoanRepository();
        var handler = new BorrowBook.Handler(loans, new FakeStudentDirectory(null));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new BorrowBook.Command(Guid.NewGuid(), "X", Due), default));

        Assert.Empty(loans.Added);
    }
}
