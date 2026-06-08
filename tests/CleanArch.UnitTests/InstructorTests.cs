using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class InstructorTests
{
    [Fact]
    public void Create_normalizes_email_and_trims_names()
    {
        var instructor = Instructor.Create("  Alan ", " Turing ", "  ALAN@UNI.EDU ", " Computing ", InstructorRank.Professor);

        Assert.Equal("Alan", instructor.FirstName);
        Assert.Equal("Turing", instructor.LastName);
        Assert.Equal("alan@uni.edu", instructor.Email);
        Assert.Equal(InstructorRank.Professor, instructor.Rank);
    }

    [Theory]
    [InlineData("", "Turing", "a@b.com")]
    [InlineData("Alan", "", "a@b.com")]
    [InlineData("Alan", "Turing", "not-an-email")]
    public void Create_with_invalid_input_throws(string first, string last, string email) =>
        Assert.Throws<DomainException>(
            () => Instructor.Create(first, last, email, "Dept", InstructorRank.Lecturer));
}
