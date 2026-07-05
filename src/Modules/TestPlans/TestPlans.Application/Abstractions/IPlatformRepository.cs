using TestPlans.Domain;

namespace TestPlans.Application.Abstractions;

public interface IPlatformRepository
{
    Task AddAsync(Platform platform, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid platformId, CancellationToken cancellationToken);
}
