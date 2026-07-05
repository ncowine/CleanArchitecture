namespace TestPlans.Domain;

/// <summary>
/// A variation a task is actioned against (e.g. PC, Xbox, PS5). Reference data shared across test plans;
/// a <see cref="TaskResult"/> is keyed by platform alongside task and version.
/// </summary>
public sealed class Platform
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;

    private Platform() { }

    private Platform(Guid id, string name, string code)
    {
        Id = id;
        Name = name;
        Code = code;
    }

    public static Platform Create(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Platform name is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Platform code is required.");

        return new Platform(Guid.NewGuid(), name.Trim(), code.Trim().ToUpperInvariant());
    }
}
