using TesterGuide.Domain;

namespace TesterGuide.Application.Abstractions;

public interface IConfigTemplateRepository
{
    Task AddAsync(ConfigTemplate configTemplate, CancellationToken cancellationToken);

    Task<ConfigTemplate?> GetAsync(Guid templateId, CancellationToken cancellationToken);
}
