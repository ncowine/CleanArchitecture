namespace TesterGuide.Domain;

/// <summary>
/// A user assigned to work a guide config. Users are authenticated principals — the assignment stores the
/// principal id and a display name, not a foreign key into a local roster.
/// </summary>
public sealed class ConfigAssignment
{
    public Guid Id { get; private set; }
    public Guid GuideConfigId { get; private set; }
    public string UserId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public ConfigRole Role { get; private set; }
    public DateTime AssignedOnUtc { get; private set; }

    private ConfigAssignment() { }

    private ConfigAssignment(
        Guid id, Guid guideConfigId, string userId, string displayName, ConfigRole role, DateTime assignedOnUtc)
    {
        Id = id;
        GuideConfigId = guideConfigId;
        UserId = userId;
        DisplayName = displayName;
        Role = role;
        AssignedOnUtc = assignedOnUtc;
    }

    public static ConfigAssignment Create(
        Guid guideConfigId, string userId, string displayName, ConfigRole role, DateTime assignedOnUtc)
    {
        if (guideConfigId == Guid.Empty)
            throw new DomainException("An assignment must reference a config.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("An assignment must reference a user.");

        return new ConfigAssignment(
            Guid.NewGuid(), guideConfigId, userId.Trim(), displayName?.Trim() ?? userId.Trim(), role, assignedOnUtc);
    }
}
