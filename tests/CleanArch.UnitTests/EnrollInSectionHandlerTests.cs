using Students.Application.Academics;
using Students.Application.Billing;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class EnrollInSectionHandlerTests
{
    private static readonly DateOnly Dob = new(2000, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static ClassSchedule AnySchedule() =>
        ClassSchedule.Create(MeetingDays.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), "Room 1");

    private static EnrollInSection.Handler HandlerFor(
        CourseSection section, Course course, Student student, FakeSectionReadService reads,
        FakeStudentAccountRepository? accounts = null)
    {
        var sections = new FakeCourseSectionRepository();
        sections.Seed(section);
        var courses = new FakeCourseRepository();
        courses.Seed(course);
        var students = new FakeStudentRepository(student);
        var charger = new AccountCharger(accounts ?? new FakeStudentAccountRepository(), students);
        return new EnrollInSection.Handler(sections, courses, students, reads, charger);
    }

    [Fact]
    public async Task Enrolls_when_no_prerequisites_and_a_seat_is_available()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS101", "Intro", null, 3, "CS");
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 2, AnySchedule());
        var handler = HandlerFor(section, course, student, new FakeSectionReadService());

        var result = await handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default);

        Assert.Equal("Enrolled", result.Status);
        Assert.Null(result.WaitlistPosition);
    }

    [Fact]
    public async Task Waitlists_when_the_section_is_full()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS101", "Intro", null, 3, "CS");
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 1, AnySchedule());
        section.Enroll(Guid.NewGuid(), Today); // fills the only seat
        var handler = HandlerFor(section, course, student, new FakeSectionReadService());

        var result = await handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default);

        Assert.Equal("Waitlisted", result.Status);
        Assert.Equal(1, result.WaitlistPosition);
    }

    [Fact]
    public async Task Throws_when_a_prerequisite_is_not_satisfied()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");
        course.AddPrerequisite(Guid.NewGuid()); // a prerequisite the student has not satisfied
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 5, AnySchedule());
        var handler = HandlerFor(section, course, student, new FakeSectionReadService());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default));
    }

    [Fact]
    public async Task Enrolls_when_the_prerequisite_is_satisfied()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");
        var prerequisiteId = Guid.NewGuid();
        course.AddPrerequisite(prerequisiteId);
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 5, AnySchedule());
        var reads = new FakeSectionReadService();
        reads.SatisfiedCourseIds.Add(prerequisiteId);
        var handler = HandlerFor(section, course, student, reads);

        var result = await handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default);

        Assert.Equal("Enrolled", result.Status);
    }

    [Fact]
    public async Task Throws_when_the_student_is_withdrawn()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        student.Withdraw();
        var course = Course.Create("CS101", "Intro", null, 3, "CS");
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 5, AnySchedule());
        var handler = HandlerFor(section, course, student, new FakeSectionReadService());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default));
    }

    [Fact]
    public async Task Taking_a_seat_charges_tuition_by_credits()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS101", "Intro", null, 3, "CS");
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 2, AnySchedule());
        var accounts = new FakeStudentAccountRepository();
        var handler = HandlerFor(section, course, student, new FakeSectionReadService(), accounts);

        await handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default);

        var account = Assert.Single(accounts.Added);
        Assert.Equal(3 * BillingPolicy.TuitionPerCredit, account.Balance);
    }

    [Fact]
    public async Task Waitlisting_does_not_charge_tuition()
    {
        var student = Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);
        var course = Course.Create("CS101", "Intro", null, 3, "CS");
        var section = CourseSection.Create(course.Id, Guid.NewGuid(), "Fall 2026", "001", 1, AnySchedule());
        section.Enroll(Guid.NewGuid(), Today); // fills the only seat
        var accounts = new FakeStudentAccountRepository();
        var handler = HandlerFor(section, course, student, new FakeSectionReadService(), accounts);

        var result = await handler.Handle(new EnrollInSection.Command(section.Id, student.Id), default);

        Assert.Equal("Waitlisted", result.Status);
        Assert.Empty(accounts.Added);
    }
}
