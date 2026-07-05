namespace TesterGuide.Domain;

/// <summary>
/// A reusable set of config defaults (focus, mode, sync) that makes creating configs quick: pick a template
/// and supply just the test plan + version + name.
/// </summary>
public sealed class ConfigTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Guid FocusId { get; private set; }
    public GuideMode Mode { get; private set; }
    public bool SyncEnabled { get; private set; }

    private ConfigTemplate() { }

    private ConfigTemplate(Guid id, string name, string description, Guid focusId, GuideMode mode, bool syncEnabled)
    {
        Id = id;
        Name = name;
        Description = description;
        FocusId = focusId;
        Mode = mode;
        SyncEnabled = syncEnabled;
    }

    public static ConfigTemplate Create(
        string name, string? description, Guid focusId, GuideMode mode, bool syncEnabled)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Template name is required.");
        if (focusId == Guid.Empty)
            throw new DomainException("A template must reference a focus.");

        return new ConfigTemplate(Guid.NewGuid(), name.Trim(), description?.Trim() ?? string.Empty, focusId, mode, syncEnabled);
    }
}
