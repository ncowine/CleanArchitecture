using Microsoft.EntityFrameworkCore;
using TestPlans.Contracts;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Reads;

/// <summary>
/// Implements the published <see cref="ITestPlanCatalog"/> against the Test Plans database. The tree is
/// materialized level by level and shaped in memory (enum-to-string and the derived version label are never
/// pushed into SQL).
/// </summary>
internal sealed class TestPlanCatalog : ITestPlanCatalog
{
    private readonly TestPlansDbContext _db;

    public TestPlanCatalog(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task<TestPlanTree?> GetTreeAsync(Guid testPlanId, CancellationToken cancellationToken)
    {
        var plan = await _db.Plans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == testPlanId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.TestPlanId == testPlanId)
            .OrderBy(c => c.Order)
            .ToListAsync(cancellationToken);

        var categoryIds = categories.Select(c => c.Id).ToList();
        var subCategories = await _db.SubCategories.AsNoTracking()
            .Where(s => categoryIds.Contains(s.CategoryId))
            .OrderBy(s => s.Order)
            .ToListAsync(cancellationToken);

        var subCategoryIds = subCategories.Select(s => s.Id).ToList();
        var tasks = await _db.Tasks.AsNoTracking()
            .Where(t => subCategoryIds.Contains(t.SubCategoryId))
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var treeCategories = categories
            .Select(c => new TreeCategory(
                c.Id, c.Name, c.Order,
                subCategories
                    .Where(s => s.CategoryId == c.Id)
                    .Select(s => new TreeSubCategory(
                        s.Id, s.Name, s.Order,
                        tasks
                            .Where(t => t.SubCategoryId == s.Id)
                            .Select(t => new TreeTask(t.Id, t.Name, t.Description, t.Mode.ToString()))
                            .ToList()))
                    .ToList()))
            .ToList();

        return new TestPlanTree(plan.Id, plan.Name, plan.Code, treeCategories);
    }

    public async Task<IReadOnlyList<VersionSummary>> GetVersionsAsync(
        Guid testPlanId, CancellationToken cancellationToken)
    {
        var versions = await _db.Versions.AsNoTracking()
            .Where(v => v.TestPlanId == testPlanId)
            .OrderBy(v => v.Version)
            .ThenBy(v => v.SubVersion)
            .ToListAsync(cancellationToken);

        return versions
            .Select(v => new VersionSummary(v.Id, v.Version, v.SubVersion, v.Label))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformSummary>> GetPlatformsAsync(CancellationToken cancellationToken)
    {
        var platforms = await _db.Platforms.AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return platforms
            .Select(p => new PlatformSummary(p.Id, p.Name, p.Code))
            .ToList();
    }

    public Task<bool> VersionExistsAsync(Guid testPlanId, Guid versionId, CancellationToken cancellationToken) =>
        _db.Versions.AnyAsync(v => v.Id == versionId && v.TestPlanId == testPlanId, cancellationToken);
}
