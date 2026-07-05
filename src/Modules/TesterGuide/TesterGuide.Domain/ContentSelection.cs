namespace TesterGuide.Domain;

/// <summary>
/// The content manager's overlay: marks a primary-system task as enabled (or not) for the tool, keyed by
/// the primary's ids. Metadata layered on the system of record without modifying it — one row per
/// (test plan, task).
/// </summary>
public sealed class ContentSelection
{
    public Guid Id { get; private set; }
    public Guid TestPlanId { get; private set; }
    public Guid TestTaskId { get; private set; }
    public bool IsEnabled { get; private set; }

    private ContentSelection() { }

    private ContentSelection(Guid id, Guid testPlanId, Guid testTaskId, bool isEnabled)
    {
        Id = id;
        TestPlanId = testPlanId;
        TestTaskId = testTaskId;
        IsEnabled = isEnabled;
    }

    public static ContentSelection Create(Guid testPlanId, Guid testTaskId, bool isEnabled)
    {
        if (testPlanId == Guid.Empty)
            throw new DomainException("A content selection must reference a test plan.");
        if (testTaskId == Guid.Empty)
            throw new DomainException("A content selection must reference a task.");

        return new ContentSelection(Guid.NewGuid(), testPlanId, testTaskId, isEnabled);
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;
}
