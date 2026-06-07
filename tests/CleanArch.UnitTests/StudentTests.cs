using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class StudentTests
{
    private static readonly DateOnly Dob = new(1990, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);

    [Fact]
    public void Create_with_valid_input_is_active_and_normalizes_email()
    {
        var student = Student.Create("Ada", "Lovelace", "  ADA@UNI.EDU ", Dob, Enrolled);

        Assert.Equal(StudentStatus.Active, student.Status);
        Assert.Equal("ada@uni.edu", student.Email);
        Assert.Equal("Ada", student.FirstName);
    }

    [Theory]
    [InlineData("", "L", "a@b.com")]
    [InlineData("F", "", "a@b.com")]
    [InlineData("F", "L", "")]
    [InlineData("F", "L", "not-an-email")]
    public void Create_with_invalid_input_throws(string first, string last, string email) =>
        Assert.Throws<DomainException>(() => Student.Create(first, last, email, Dob, Enrolled));

    [Fact]
    public void Create_with_dob_not_before_enrollment_throws() =>
        Assert.Throws<DomainException>(() => Student.Create("F", "L", "a@b.com", Enrolled, Enrolled));

    [Fact]
    public void Withdraw_sets_status_withdrawn()
    {
        var student = Student.Create("F", "L", "a@b.com", Dob, Enrolled);
        student.Withdraw();
        Assert.Equal(StudentStatus.Withdrawn, student.Status);
    }

    [Fact]
    public void Hold_requires_originating_message_id() =>
        Assert.Throws<DomainException>(() => StudentHold.Place(Guid.Empty, Guid.NewGuid(), "reason", DateTime.UtcNow));

    [Fact]
    public void Hold_place_trims_reason_and_keeps_message_id()
    {
        var messageId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var hold = StudentHold.Place(messageId, studentId, "  too many fines ", DateTime.UtcNow);

        Assert.Equal(messageId, hold.Id);
        Assert.Equal(studentId, hold.StudentId);
        Assert.Equal("too many fines", hold.Reason);
    }
}
