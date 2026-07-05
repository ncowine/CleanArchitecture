using TesterGuide.Domain;

namespace TesterGuide.Application.Abstractions;

public interface IGuideConfigRepository
{
    Task AddAsync(GuideConfig config, CancellationToken cancellationToken);

    Task<GuideConfig?> GetAsync(Guid configId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid configId, CancellationToken cancellationToken);

    Task AddAssignmentAsync(ConfigAssignment assignment, CancellationToken cancellationToken);

    Task<bool> AssignmentExistsAsync(Guid configId, string userId, CancellationToken cancellationToken);
}
