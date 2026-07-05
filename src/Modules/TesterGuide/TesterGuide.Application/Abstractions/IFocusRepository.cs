using TesterGuide.Domain;

namespace TesterGuide.Application.Abstractions;

public interface IFocusRepository
{
    Task AddAsync(Focus focus, CancellationToken cancellationToken);

    Task<Focus?> GetAsync(Guid focusId, CancellationToken cancellationToken);

    void Remove(Focus focus);

    Task<bool> ExistsAsync(Guid focusId, CancellationToken cancellationToken);
}
