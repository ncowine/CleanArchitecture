namespace Students.Domain;

/// <summary>
/// A catalog course offered by the college. Aggregate root. <see cref="CourseSection"/>s reference it by
/// id; other courses may list it as a prerequisite (<see cref="CoursePrerequisite"/>).
/// </summary>
public sealed class Course
{
    private readonly List<CoursePrerequisite> _prerequisites = [];

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public int Credits { get; private set; }
    public string DepartmentName { get; private set; } = null!;

    public IReadOnlyCollection<CoursePrerequisite> Prerequisites => _prerequisites.AsReadOnly();

    private Course() { }

    private Course(Guid id, string code, string title, string? description, int credits, string departmentName)
    {
        Id = id;
        Code = code;
        Title = title;
        Description = description;
        Credits = credits;
        DepartmentName = departmentName;
    }

    public static Course Create(string code, string title, string? description, int credits, string departmentName)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Course code is required.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Course title is required.");
        if (credits is < 1 or > 12)
            throw new DomainException("Credits must be between 1 and 12.");
        if (string.IsNullOrWhiteSpace(departmentName))
            throw new DomainException("Department name is required.");

        return new Course(
            id: Guid.NewGuid(),
            code: code.Trim().ToUpperInvariant(),
            title: title.Trim(),
            description: string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            credits: credits,
            departmentName: departmentName.Trim());
    }

    public void AddPrerequisite(Guid prerequisiteCourseId)
    {
        if (prerequisiteCourseId == Guid.Empty)
            throw new DomainException("A prerequisite course is required.");
        if (prerequisiteCourseId == Id)
            throw new DomainException("A course cannot be its own prerequisite.");
        if (_prerequisites.Any(prerequisite => prerequisite.PrerequisiteCourseId == prerequisiteCourseId))
            throw new DomainException("That prerequisite is already listed for this course.");

        _prerequisites.Add(new CoursePrerequisite(prerequisiteCourseId));
    }

    public void RemovePrerequisite(Guid prerequisiteCourseId)
    {
        var existing = _prerequisites.FirstOrDefault(p => p.PrerequisiteCourseId == prerequisiteCourseId)
            ?? throw new DomainException("That prerequisite is not listed for this course.");

        _prerequisites.Remove(existing);
    }
}
