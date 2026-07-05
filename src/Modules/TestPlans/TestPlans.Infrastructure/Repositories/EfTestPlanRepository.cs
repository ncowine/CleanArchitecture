using Microsoft.EntityFrameworkCore;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Repositories;

internal sealed class EfTestPlanRepository : ITestPlanRepository
{
    private readonly TestPlansDbContext _db;

    public EfTestPlanRepository(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TestPlan plan, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Plans.AddAsync(plan, cancellationToken);
    }

    public async Task AddVersionAsync(TestPlanVersion version, CancellationToken cancellationToken)
    {
        await _db.Versions.AddAsync(version, cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid testPlanId, CancellationToken cancellationToken) =>
        _db.Plans.AnyAsync(plan => plan.Id == testPlanId, cancellationToken);

    public Task<bool> VersionExistsAsync(Guid versionId, CancellationToken cancellationToken) =>
        _db.Versions.AnyAsync(version => version.Id == versionId, cancellationToken);
}
