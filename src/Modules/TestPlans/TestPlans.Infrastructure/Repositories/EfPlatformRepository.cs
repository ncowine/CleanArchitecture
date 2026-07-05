using Microsoft.EntityFrameworkCore;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Repositories;

internal sealed class EfPlatformRepository : IPlatformRepository
{
    private readonly TestPlansDbContext _db;

    public EfPlatformRepository(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Platform platform, CancellationToken cancellationToken)
    {
        await _db.Platforms.AddAsync(platform, cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid platformId, CancellationToken cancellationToken) =>
        _db.Platforms.AnyAsync(platform => platform.Id == platformId, cancellationToken);
}
