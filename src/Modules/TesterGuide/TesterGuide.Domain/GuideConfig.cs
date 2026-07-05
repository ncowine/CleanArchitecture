namespace TesterGuide.Domain;

/// <summary>
/// The central Tester Guide aggregate: a config dedicates the tool to one test plan at a specific version,
/// with a focus, a play mode, and whether its actions sync back to the primary action log. It references
/// the primary system (test plan / version) only by key — those live in another module and database.
/// </summary>
public sealed class GuideConfig
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid TestPlanId { get; private set; }
    public Guid TestPlanVersionId { get; private set; }
    public Guid FocusId { get; private set; }
    public GuideMode Mode { get; private set; }
    public bool SyncEnabled { get; private set; }
    public ConfigStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;

    private GuideConfig() { }

    private GuideConfig(
        Guid id, string name, Guid testPlanId, Guid testPlanVersionId,
        Guid focusId, GuideMode mode, bool syncEnabled, string createdBy)
    {
        Id = id;
        Name = name;
        TestPlanId = testPlanId;
        TestPlanVersionId = testPlanVersionId;
        FocusId = focusId;
        Mode = mode;
        SyncEnabled = syncEnabled;
        Status = ConfigStatus.Draft;
        CreatedBy = createdBy;
    }

    public static GuideConfig Create(
        string name, Guid testPlanId, Guid testPlanVersionId,
        Guid focusId, GuideMode mode, bool syncEnabled, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Config name is required.");
        if (testPlanId == Guid.Empty)
            throw new DomainException("A config must reference a test plan.");
        if (testPlanVersionId == Guid.Empty)
            throw new DomainException("A config must reference a test plan version.");
        if (focusId == Guid.Empty)
            throw new DomainException("A config must have a focus.");
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new DomainException("A config must record who created it.");

        return new GuideConfig(
            Guid.NewGuid(), name.Trim(), testPlanId, testPlanVersionId, focusId, mode, syncEnabled, createdBy);
    }

    public void Activate()
    {
        if (Status == ConfigStatus.Closed)
            throw new DomainException("A closed config cannot be reactivated.");

        Status = ConfigStatus.Active;
    }

    public void Close() => Status = ConfigStatus.Closed;
}
