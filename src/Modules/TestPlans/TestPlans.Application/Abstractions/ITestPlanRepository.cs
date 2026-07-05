using TestPlans.Domain;

namespace TestPlans.Application.Abstractions;

public interface ITestPlanRepository
{
    Task AddAsync(TestPlan plan, CancellationToken cancellationToken);

    Task AddVersionAsync(TestPlanVersion version, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid testPlanId, CancellationToken cancellationToken);

    Task<bool> VersionExistsAsync(Guid versionId, CancellationToken cancellationToken);
}
