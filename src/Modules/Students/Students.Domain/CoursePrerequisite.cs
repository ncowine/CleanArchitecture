namespace Students.Domain;

/// <summary>
/// A prerequisite link: a course that must be satisfied before enrolling in the owning course. Part of the
/// <see cref="Course"/> aggregate — references the prerequisite course by id only.
/// </summary>
public sealed class CoursePrerequisite
{
    public Guid Id { get; private set; }
    public Guid PrerequisiteCourseId { get; private set; }

    private CoursePrerequisite() { }

    internal CoursePrerequisite(Guid prerequisiteCourseId)
    {
        Id = Guid.NewGuid();
        PrerequisiteCourseId = prerequisiteCourseId;
    }
}
