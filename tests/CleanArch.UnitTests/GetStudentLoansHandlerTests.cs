using Library.Application.Loans;
using Library.Domain;
using Students.Contracts;
using Xunit;

namespace CleanArch.UnitTests;

public class GetStudentLoansHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly DateOnly Due = new(2026, 3, 1);

    [Fact]
    public async Task Composes_student_identity_with_their_loans()
    {
        var studentId = Guid.NewGuid();
        var directory = new FakeStudentDirectory(new StudentSummary(studentId, "Grace Hopper", "g@uni.edu", "Active"));
        var loans = new FakeLoanRepository();
        loans.Seed(Loan.Borrow(studentId, "TAOCP", Today, Due));
        var handler = new GetStudentLoans.Handler(loans, directory);

        var response = await handler.Handle(new GetStudentLoans.Query(studentId), default);

        Assert.Equal(studentId, response.StudentId);
        Assert.Equal("Grace Hopper", response.StudentName);
        Assert.Equal("Active", response.Status);
        Assert.Single(response.Loans);
        Assert.Equal("TAOCP", response.Loans[0].BookTitle);
    }

    [Fact]
    public async Task Unknown_student_throws()
    {
        var handler = new GetStudentLoans.Handler(new FakeLoanRepository(), new FakeStudentDirectory(null));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new GetStudentLoans.Query(Guid.NewGuid()), default));
    }
}
