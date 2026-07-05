using TesterGuide.Domain;

namespace TesterGuide.Application.Abstractions;

public interface IContentSelectionRepository
{
    Task<ContentSelection?> GetAsync(Guid testPlanId, Guid testTaskId, CancellationToken cancellationToken);

    Task AddAsync(ContentSelection selection, CancellationToken cancellationToken);
}
