namespace TestPlans.Domain;

/// <summary>
/// A test plan: the root of the content tree (categories → sub-categories → tasks) and the owner of its
/// own versions. A separate aggregate root — categories, versions, and results reference it by id only.
/// </summary>
public sealed class TestPlan
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;

    private TestPlan() { }

    private TestPlan(Guid id, string name, string code)
    {
        Id = id;
        Name = name;
        Code = code;
    }

    public static TestPlan Create(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Test plan name is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Test plan code is required.");

        return new TestPlan(Guid.NewGuid(), name.Trim(), code.Trim().ToUpperInvariant());
    }
}
