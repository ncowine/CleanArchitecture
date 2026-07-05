namespace TestPlans.Contracts;

/// <summary>A task node in a published test-plan tree.</summary>
public sealed record TreeTask(Guid Id, string Name, string Description, string Mode);

/// <summary>A sub-category node with its tasks.</summary>
public sealed record TreeSubCategory(Guid Id, string Name, int Order, IReadOnlyList<TreeTask> Tasks);

/// <summary>A category node with its sub-categories.</summary>
public sealed record TreeCategory(Guid Id, string Name, int Order, IReadOnlyList<TreeSubCategory> SubCategories);

/// <summary>The full content tree of a test plan, published for other modules to render against.</summary>
public sealed record TestPlanTree(Guid Id, string Name, string Code, IReadOnlyList<TreeCategory> Categories);

/// <summary>A published (Version.SubVersion) summary for a test plan.</summary>
public sealed record VersionSummary(Guid Id, int Version, int SubVersion, string Label);

/// <summary>A published platform (variation) summary.</summary>
public sealed record PlatformSummary(Guid Id, string Name, string Code);

/// <summary>
/// The Test Plans module's published read contract. Lets another module (the Tester Guide) read the
/// content tree, versions, and platforms by key without referencing this module's domain, application, or
/// <c>DbContext</c> — and, in turn, without reaching across the Test Plans database boundary. The
/// implementation lives in TestPlans.Infrastructure and owns the DB access.
/// </summary>
public interface ITestPlanCatalog
{
    Task<TestPlanTree?> GetTreeAsync(Guid testPlanId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VersionSummary>> GetVersionsAsync(Guid testPlanId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformSummary>> GetPlatformsAsync(CancellationToken cancellationToken);

    Task<bool> VersionExistsAsync(Guid testPlanId, Guid versionId, CancellationToken cancellationToken);
}
