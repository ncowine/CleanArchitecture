using Library.Application.Loans;
using Library.Domain;
using Students.Contracts;
using Xunit;

namespace CleanArch.UnitTests;

public class GetStudentLoansHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly DateOnly Due = new(2026, 1, 22);

    [Fact]
    public async Task Composes_student_identity_with_their_loans_and_resolves_titles()
    {
        var studentId = Guid.NewGuid();
        var copyId = Guid.NewGuid();
        var directory = new FakeStudentDirectory(new StudentSummary(studentId, "Grace Hopper", "g@uni.edu", "Active"));
        var loans = new FakeLoanRepository();
        loans.Seed(Loan.Borrow(studentId, copyId, Today, Due));
        var books = new FakeBookReadService();
        books.Titles[copyId] = "TAOCP";
        var audit = new FakeAuditRecorder();
        var handler = new GetStudentLoans.Handler(loans, books, directory, audit);

        var response = await handler.Handle(new GetStudentLoans.Query(studentId), default);

        Assert.Equal(studentId, response.StudentId);
        Assert.Equal("Grace Hopper", response.StudentName);
        Assert.Single(response.Loans.Items);
        Assert.Equal(1, response.Loans.TotalCount);
        Assert.Equal("TAOCP", response.Loans.Items[0].BookTitle);

        // The read is audited: the record says whose data was read and how much of it came back.
        Assert.Equal($"Student/{studentId}", new GetStudentLoans.Query(studentId).AuditResource);
        Assert.Equal("1", audit.Annotations["loansReturned"]);
        Assert.Equal("Module:Students", audit.Annotations["identitySource"]);
    }

    [Fact]
    public async Task Unknown_student_throws()
    {
        var handler = new GetStudentLoans.Handler(
            new FakeLoanRepository(), new FakeBookReadService(), new FakeStudentDirectory(null), new FakeAuditRecorder());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new GetStudentLoans.Query(Guid.NewGuid()), default));
    }
}
