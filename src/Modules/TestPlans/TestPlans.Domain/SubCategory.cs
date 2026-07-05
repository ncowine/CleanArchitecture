namespace TestPlans.Domain;

/// <summary>A grouping of tasks within a <see cref="Category"/>.</summary>
public sealed class SubCategory
{
    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Order { get; private set; }

    private SubCategory() { }

    private SubCategory(Guid id, Guid categoryId, string name, int order)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Order = order;
    }

    public static SubCategory Create(Guid categoryId, string name, int order)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("A sub-category must belong to a category.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Sub-category name is required.");
        if (order < 0)
            throw new DomainException("Sub-category order cannot be negative.");

        return new SubCategory(Guid.NewGuid(), categoryId, name.Trim(), order);
    }
}
