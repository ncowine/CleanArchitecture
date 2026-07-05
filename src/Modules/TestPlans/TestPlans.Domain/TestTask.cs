namespace TestPlans.Domain;

/// <summary>
/// A single unit of test work within a <see cref="SubCategory"/>. Named <c>TestTask</c> rather than
/// <c>Task</c> to avoid shadowing <see cref="System.Threading.Tasks.Task"/> throughout the codebase.
/// </summary>
public sealed class TestTask
{
    public Guid Id { get; private set; }
    public Guid SubCategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public TaskMode Mode { get; private set; }

    private TestTask() { }

    private TestTask(Guid id, Guid subCategoryId, string name, string description, TaskMode mode)
    {
        Id = id;
        SubCategoryId = subCategoryId;
        Name = name;
        Description = description;
        Mode = mode;
    }

    public static TestTask Create(Guid subCategoryId, string name, string? description, TaskMode mode)
    {
        if (subCategoryId == Guid.Empty)
            throw new DomainException("A task must belong to a sub-category.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Task name is required.");

        return new TestTask(Guid.NewGuid(), subCategoryId, name.Trim(), description?.Trim() ?? string.Empty, mode);
    }
}
