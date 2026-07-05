using BuildingBlocks.Messaging;
using TesterGuide.Application.Abstractions;

namespace TesterGuide.Application.Actions;

/// <summary>List a config's action log, including each action's sync state (and any rejection reason).</summary>
public static class ListConfigActions
{
    public sealed record ActionView(
        Guid Id,
        Guid TestTaskId,
        Guid PlatformId,
        Guid TestPlanVersionId,
        string Status,
        string UserId,
        DateTime OccurredOnUtc,
        string SyncState,
        string? SyncError);

    public sealed record Query(Guid GuideConfigId) : IRequest<IReadOnlyList<ActionView>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<ActionView>>
    {
        private readonly IGuideReadService _reads;

        public Handler(IGuideReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<ActionView>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.ListActionsAsync(query.GuideConfigId, cancellationToken);
    }
}
