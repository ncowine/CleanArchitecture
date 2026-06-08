using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class CourseSectionTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static CourseSection Section(int capacity) =>
        CourseSection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Fall 2026",
            "001",
            capacity,
            ClassSchedule.Create(MeetingDays.Monday | MeetingDays.Wednesday, new TimeOnly(10, 0), new TimeOnly(11, 0), "Room 101"));

    [Fact]
    public void Enroll_takes_a_seat_when_capacity_is_available()
    {
        var section = Section(capacity: 2);

        var enrollment = section.Enroll(Guid.NewGuid(), Today);

        Assert.Equal(SectionEnrollmentStatus.Enrolled, enrollment.Status);
        Assert.Null(enrollment.WaitlistPosition);
        Assert.Equal(1, section.EnrolledCount);
    }

    [Fact]
    public void Enroll_past_capacity_waitlists_with_a_position()
    {
        var section = Section(capacity: 1);
        section.Enroll(Guid.NewGuid(), Today);

        var waitlisted = section.Enroll(Guid.NewGuid(), Today);

        Assert.Equal(SectionEnrollmentStatus.Waitlisted, waitlisted.Status);
        Assert.Equal(1, waitlisted.WaitlistPosition);
        Assert.Equal(1, section.EnrolledCount);
        Assert.Equal(1, section.WaitlistCount);
    }

    [Fact]
    public void Dropping_an_enrolled_student_promotes_the_head_of_the_waitlist()
    {
        var section = Section(capacity: 1);
        var seated = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        section.Enroll(seated, Today);
        section.Enroll(second, Today); // waitlist 1
        section.Enroll(third, Today);  // waitlist 2

        section.Drop(seated);

        Assert.Equal(1, section.EnrolledCount);
        Assert.Equal(SectionEnrollmentStatus.Enrolled, section.Roster.First(e => e.StudentId == second).Status);

        var thirdEntry = section.Roster.First(e => e.StudentId == third);
        Assert.Equal(SectionEnrollmentStatus.Waitlisted, thirdEntry.Status);
        Assert.Equal(1, thirdEntry.WaitlistPosition); // renumbered after the promotion
    }

    [Fact]
    public void Dropping_a_waitlisted_student_renumbers_those_behind()
    {
        var section = Section(capacity: 1);
        section.Enroll(Guid.NewGuid(), Today);
        var dropped = Guid.NewGuid();
        var behind = Guid.NewGuid();
        section.Enroll(dropped, Today); // waitlist 1
        section.Enroll(behind, Today);  // waitlist 2

        section.Drop(dropped);

        Assert.Equal(1, section.Roster.First(e => e.StudentId == behind).WaitlistPosition);
        Assert.Equal(1, section.EnrolledCount); // the seated student is untouched
    }

    [Fact]
    public void Enrolling_the_same_student_twice_throws()
    {
        var section = Section(capacity: 5);
        var studentId = Guid.NewGuid();
        section.Enroll(studentId, Today);

        Assert.Throws<DomainException>(() => section.Enroll(studentId, Today));
    }

    [Fact]
    public void Enrolling_in_a_cancelled_section_throws()
    {
        var section = Section(capacity: 5);
        section.Cancel();

        Assert.Throws<DomainException>(() => section.Enroll(Guid.NewGuid(), Today));
    }

    [Fact]
    public void Dropping_a_student_not_on_the_roster_throws()
    {
        var section = Section(capacity: 5);

        Assert.Throws<DomainException>(() => section.Drop(Guid.NewGuid()));
    }

    [Fact]
    public void Recording_a_grade_completes_the_enrollment()
    {
        var section = Section(capacity: 5);
        var studentId = Guid.NewGuid();
        section.Enroll(studentId, Today);

        section.RecordGrade(studentId, Grade.FromLetter("B+"));

        var entry = section.Roster.First(e => e.StudentId == studentId);
        Assert.Equal(SectionEnrollmentStatus.Completed, entry.Status);
        Assert.Equal("B+", entry.Grade!.Letter);
        Assert.Equal(0, section.EnrolledCount); // no longer occupying an active seat
    }

    [Fact]
    public void Grading_a_student_who_is_not_enrolled_throws()
    {
        var section = Section(capacity: 5);

        Assert.Throws<DomainException>(() => section.RecordGrade(Guid.NewGuid(), Grade.FromLetter("A")));
    }
}
