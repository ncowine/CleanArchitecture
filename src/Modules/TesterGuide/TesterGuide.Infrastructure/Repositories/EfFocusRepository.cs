using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Repositories;

internal sealed class EfFocusRepository : IFocusRepository
{
    private readonly TesterGuideDbContext _db;

    public EfFocusRepository(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Focus focus, CancellationToken cancellationToken)
    {
        await _db.Focuses.AddAsync(focus, cancellationToken);
    }

    // Tracked (no AsNoTracking): callers mutate/remove the returned entity within the unit of work.
    public Task<Focus?> GetAsync(Guid focusId, CancellationToken cancellationToken) =>
        _db.Focuses.FirstOrDefaultAsync(focus => focus.Id == focusId, cancellationToken);

    public void Remove(Focus focus) => _db.Focuses.Remove(focus);

    public Task<bool> ExistsAsync(Guid focusId, CancellationToken cancellationToken) =>
        _db.Focuses.AnyAsync(focus => focus.Id == focusId, cancellationToken);
}
