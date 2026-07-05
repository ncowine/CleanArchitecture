using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Repositories;

internal sealed class EfGuideConfigRepository : IGuideConfigRepository
{
    private readonly TesterGuideDbContext _db;

    public EfGuideConfigRepository(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(GuideConfig config, CancellationToken cancellationToken)
    {
        await _db.Configs.AddAsync(config, cancellationToken);
    }

    public Task<GuideConfig?> GetAsync(Guid configId, CancellationToken cancellationToken) =>
        _db.Configs.FirstOrDefaultAsync(config => config.Id == configId, cancellationToken);

    public Task<bool> ExistsAsync(Guid configId, CancellationToken cancellationToken) =>
        _db.Configs.AnyAsync(config => config.Id == configId, cancellationToken);

    public async Task AddAssignmentAsync(ConfigAssignment assignment, CancellationToken cancellationToken)
    {
        await _db.Assignments.AddAsync(assignment, cancellationToken);
    }

    public Task<bool> AssignmentExistsAsync(Guid configId, string userId, CancellationToken cancellationToken) =>
        _db.Assignments.AnyAsync(a => a.GuideConfigId == configId && a.UserId == userId, cancellationToken);
}
