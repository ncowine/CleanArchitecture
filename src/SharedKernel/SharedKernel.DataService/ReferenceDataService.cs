using Microsoft.EntityFrameworkCore;
using SharedKernel.Data;
using SharedKernel.Models;

namespace SharedKernel.DataService;

/// <summary>The uncached database read behind <see cref="CachedReferenceDataService"/> — the "miss" path.</summary>
internal sealed class ReferenceDataService : IReferenceDataService
{
    private readonly ReferenceDbContext _db;

    public ReferenceDataService(ReferenceDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Site>> GetSitesAsync(CancellationToken cancellationToken) =>
        await _db.Sites.AsNoTracking().OrderBy(site => site.Name).ToListAsync(cancellationToken);

    public Task<Site?> GetSiteAsync(Guid siteId, CancellationToken cancellationToken) =>
        _db.Sites.AsNoTracking().FirstOrDefaultAsync(site => site.Id == siteId, cancellationToken);
}
