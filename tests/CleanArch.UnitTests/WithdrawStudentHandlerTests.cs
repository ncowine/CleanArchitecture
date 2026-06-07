using Students.Application.Students;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class WithdrawStudentHandlerTests
{
    private static readonly DateOnly Dob = new(1990, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);

    [Fact]
    public async Task Withdraws_the_student_and_invalidates_the_cache()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var repository = new FakeStudentRepository(student);
        var cache = new FakeStudentCacheInvalidator();
        var handler = new WithdrawStudent.Handler(repository, cache);

        await handler.Handle(new WithdrawStudent.Command(student.Id), default);

        Assert.Equal(StudentStatus.Withdrawn, student.Status);
        Assert.Contains(student.Id, cache.Removed);
    }

    [Fact]
    public async Task Unknown_student_throws()
    {
        var handler = new WithdrawStudent.Handler(new FakeStudentRepository(null), new FakeStudentCacheInvalidator());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new WithdrawStudent.Command(Guid.NewGuid()), default));
    }
}
