using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class GradeTests
{
    [Theory]
    [InlineData("A", 4.0)]
    [InlineData("A-", 3.7)]
    [InlineData("B+", 3.3)]
    [InlineData("C", 2.0)]
    [InlineData("D-", 0.7)]
    [InlineData("F", 0.0)]
    public void FromLetter_maps_to_points(string letter, double expectedPoints)
    {
        var grade = Grade.FromLetter(letter);

        Assert.Equal(letter, grade.Letter);
        Assert.Equal((decimal)expectedPoints, grade.Points);
    }

    [Fact]
    public void FromLetter_is_case_insensitive_and_trims()
    {
        var grade = Grade.FromLetter("  a- ");

        Assert.Equal("A-", grade.Letter);
        Assert.Equal(3.7m, grade.Points);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A+")]
    [InlineData("E")]
    [InlineData("Z")]
    public void FromLetter_with_an_invalid_grade_throws(string letter) =>
        Assert.Throws<DomainException>(() => Grade.FromLetter(letter));

    [Fact]
    public void F_does_not_earn_credit_but_a_passing_grade_does()
    {
        Assert.False(Grade.FromLetter("F").IsPassing);
        Assert.True(Grade.FromLetter("D-").IsPassing);
        Assert.True(Grade.FromLetter("A").IsPassing);
    }
}
