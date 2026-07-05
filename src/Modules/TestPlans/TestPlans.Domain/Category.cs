namespace TestPlans.Domain;

/// <summary>A top-level grouping of sub-categories within a <see cref="TestPlan"/>.</summary>
public sealed class Category
{
    public Guid Id { get; private set; }
    public Guid TestPlanId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Order { get; private set; }

    private Category() { }

    private Category(Guid id, Guid testPlanId, string name, int order)
    {
        Id = id;
        TestPlanId = testPlanId;
        Name = name;
        Order = order;
    }

    public static Category Create(Guid testPlanId, string name, int order)
    {
        if (testPlanId == Guid.Empty)
            throw new DomainException("A category must belong to a test plan.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");
        if (order < 0)
            throw new DomainException("Category order cannot be negative.");

        return new Category(Guid.NewGuid(), testPlanId, name.Trim(), order);
    }
}
