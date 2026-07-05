using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Repositories;

internal sealed class EfContentSelectionRepository : IContentSelectionRepository
{
    private readonly TesterGuideDbContext _db;

    public EfContentSelectionRepository(TesterGuideDbContext db)
    {
        _db = db;
    }

    // Tracked: the caller flips IsEnabled on the returned row within the unit of work (upsert).
    public Task<ContentSelection?> GetAsync(Guid testPlanId, Guid testTaskId, CancellationToken cancellationToken) =>
        _db.ContentSelections.FirstOrDefaultAsync(
            s => s.TestPlanId == testPlanId && s.TestTaskId == testTaskId, cancellationToken);

    public async Task AddAsync(ContentSelection selection, CancellationToken cancellationToken)
    {
        await _db.ContentSelections.AddAsync(selection, cancellationToken);
    }
}
