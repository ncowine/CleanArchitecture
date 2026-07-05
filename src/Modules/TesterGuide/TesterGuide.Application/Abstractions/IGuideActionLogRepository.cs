using TesterGuide.Domain;

namespace TesterGuide.Application.Abstractions;

public interface IGuideActionLogRepository
{
    Task AddAsync(GuideActionLogEntry entry, CancellationToken cancellationToken);
}
