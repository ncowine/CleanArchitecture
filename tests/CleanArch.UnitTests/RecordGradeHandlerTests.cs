using Students.Application.Academics;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class RecordGradeHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static ClassSchedule AnySchedule() =>
        ClassSchedule.Create(MeetingDays.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), "Room 1");

    [Fact]
    public async Task Records_the_grade_and_returns_its_points()
    {
        var studentId = Guid.NewGuid();
        var section = CourseSection.Create(Guid.NewGuid(), Guid.NewGuid(), "Fall 2026", "001", 5, AnySchedule());
        section.Enroll(studentId, Today);
        var sections = new FakeCourseSectionRepository();
        sections.Seed(section);
        var handler = new RecordGrade.Handler(sections);

        var result = await handler.Handle(new RecordGrade.Command(section.Id, studentId, "A-"), default);

        Assert.Equal("A-", result.Grade);
        Assert.Equal(3.7m, result.Points);
        Assert.Equal(SectionEnrollmentStatus.Completed, section.Roster.First(e => e.StudentId == studentId).Status);
    }

    [Fact]
    public async Task Unknown_section_throws()
    {
        var handler = new RecordGrade.Handler(new FakeCourseSectionRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new RecordGrade.Command(Guid.NewGuid(), Guid.NewGuid(), "A"), default));
    }

    [Fact]
    public async Task Invalid_letter_grade_throws()
    {
        var studentId = Guid.NewGuid();
        var section = CourseSection.Create(Guid.NewGuid(), Guid.NewGuid(), "Fall 2026", "001", 5, AnySchedule());
        section.Enroll(studentId, Today);
        var sections = new FakeCourseSectionRepository();
        sections.Seed(section);
        var handler = new RecordGrade.Handler(sections);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new RecordGrade.Command(section.Id, studentId, "Z"), default));
    }
}
