using BuildingBlocks.Pagination;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Read access to outbox messages that exhausted their delivery attempts and were parked. Exposed so
/// operators can inspect what failed (and why) and decide whether to replay or discard it.
/// </summary>
public interface IDeadLetterReader
{
    Task<PagedResult<DeadLetterEntry>> GetAsync(int page, int pageSize, CancellationToken cancellationToken);
}
