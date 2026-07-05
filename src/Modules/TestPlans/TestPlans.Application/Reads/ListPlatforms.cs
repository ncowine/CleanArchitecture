using BuildingBlocks.Messaging;
using TestPlans.Contracts;

namespace TestPlans.Application.Reads;

/// <summary>List the shared platforms (variations).</summary>
public static class ListPlatforms
{
    public sealed record Query : IRequest<IReadOnlyList<PlatformSummary>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<PlatformSummary>>
    {
        private readonly ITestPlanCatalog _catalog;

        public Handler(ITestPlanCatalog catalog)
        {
            _catalog = catalog;
        }

        public Task<IReadOnlyList<PlatformSummary>> Handle(Query query, CancellationToken cancellationToken) =>
            _catalog.GetPlatformsAsync(cancellationToken);
    }
}
