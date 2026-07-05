using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Repositories;

internal sealed class EfConfigTemplateRepository : IConfigTemplateRepository
{
    private readonly TesterGuideDbContext _db;

    public EfConfigTemplateRepository(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ConfigTemplate configTemplate, CancellationToken cancellationToken)
    {
        await _db.Templates.AddAsync(configTemplate, cancellationToken);
    }

    public Task<ConfigTemplate?> GetAsync(Guid templateId, CancellationToken cancellationToken) =>
        _db.Templates.FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
}
