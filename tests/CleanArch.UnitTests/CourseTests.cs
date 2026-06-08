using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class CourseTests
{
    [Fact]
    public void Create_uppercases_the_code_and_trims()
    {
        var course = Course.Create("  cs101 ", "  Intro to CS ", null, 3, " Computer Science ");

        Assert.Equal("CS101", course.Code);
        Assert.Equal("Intro to CS", course.Title);
        Assert.Equal("Computer Science", course.DepartmentName);
    }

    [Theory]
    [InlineData("", "Title", 3)]
    [InlineData("CODE", "", 3)]
    [InlineData("CODE", "Title", 0)]
    [InlineData("CODE", "Title", 13)]
    public void Create_with_invalid_input_throws(string code, string title, int credits) =>
        Assert.Throws<DomainException>(() => Course.Create(code, title, null, credits, "Dept"));

    [Fact]
    public void AddPrerequisite_lists_it()
    {
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");
        var prerequisiteId = Guid.NewGuid();

        course.AddPrerequisite(prerequisiteId);

        Assert.Contains(course.Prerequisites, p => p.PrerequisiteCourseId == prerequisiteId);
    }

    [Fact]
    public void A_course_cannot_be_its_own_prerequisite()
    {
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");

        Assert.Throws<DomainException>(() => course.AddPrerequisite(course.Id));
    }

    [Fact]
    public void Duplicate_prerequisite_throws()
    {
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");
        var prerequisiteId = Guid.NewGuid();
        course.AddPrerequisite(prerequisiteId);

        Assert.Throws<DomainException>(() => course.AddPrerequisite(prerequisiteId));
    }

    [Fact]
    public void Removing_an_absent_prerequisite_throws()
    {
        var course = Course.Create("CS201", "Data Structures", null, 3, "CS");

        Assert.Throws<DomainException>(() => course.RemovePrerequisite(Guid.NewGuid()));
    }
}
