using Students.Application.Outbox;
using Students.Application.Students;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class WithdrawStudentHandlerTests
{
    private static readonly DateOnly Dob = new(1990, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);

    [Fact]
    public async Task Withdraws_the_student_invalidates_the_cache_and_publishes_the_event()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var repository = new FakeStudentRepository(student);
        var cache = new FakeStudentCacheInvalidator();
        var outbox = new FakeStudentOutbox();
        var handler = new WithdrawStudent.Handler(repository, cache, outbox);

        await handler.Handle(new WithdrawStudent.Command(student.Id), default);

        Assert.Equal(StudentStatus.Withdrawn, student.Status);
        Assert.Contains(student.Id, cache.Removed);

        var @event = Assert.Single(outbox.Events);
        var withdrawn = Assert.IsType<StudentWithdrawn>(@event);
        Assert.Equal(student.Id, withdrawn.StudentId);
    }

    [Fact]
    public async Task Withdrawing_an_already_withdrawn_student_does_not_republish()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        student.Withdraw();
        var outbox = new FakeStudentOutbox();
        var handler = new WithdrawStudent.Handler(
            new FakeStudentRepository(student), new FakeStudentCacheInvalidator(), outbox);

        await handler.Handle(new WithdrawStudent.Command(student.Id), default);

        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Unknown_student_throws()
    {
        var handler = new WithdrawStudent.Handler(
            new FakeStudentRepository(null), new FakeStudentCacheInvalidator(), new FakeStudentOutbox());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new WithdrawStudent.Command(Guid.NewGuid()), default));
    }
}
