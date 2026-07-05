namespace TesterGuide.Domain;

/// <summary>
/// A named label testers concentrate on, attached to a config for organization and reporting. A managed
/// list (create/update/delete) — it does not itself filter content.
/// </summary>
public sealed class Focus
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Focus() { }

    private Focus(Guid id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public static Focus Create(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Focus name is required.");

        return new Focus(Guid.NewGuid(), name.Trim(), description?.Trim() ?? string.Empty);
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Focus name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }
}
