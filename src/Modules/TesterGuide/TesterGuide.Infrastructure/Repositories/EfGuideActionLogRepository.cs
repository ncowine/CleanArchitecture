using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Repositories;

internal sealed class EfGuideActionLogRepository : IGuideActionLogRepository
{
    private readonly TesterGuideDbContext _db;

    public EfGuideActionLogRepository(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(GuideActionLogEntry entry, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.ActionLog.AddAsync(entry, cancellationToken);
    }
}
