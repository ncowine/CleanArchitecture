using BuildingBlocks.Messaging;
using TesterGuide.Application.Abstractions;

namespace TesterGuide.Application.Content;

/// <summary>List the content-manager overlay for a test plan (which tasks are enabled for the tool).</summary>
public static class ListContentSelections
{
    public sealed record ContentSelectionView(Guid Id, Guid TestPlanId, Guid TestTaskId, bool IsEnabled);

    public sealed record Query(Guid TestPlanId) : IRequest<IReadOnlyList<ContentSelectionView>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<ContentSelectionView>>
    {
        private readonly IGuideReadService _reads;

        public Handler(IGuideReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<ContentSelectionView>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.ListContentAsync(query.TestPlanId, cancellationToken);
    }
}
