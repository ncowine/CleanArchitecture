using BuildingBlocks.Messaging;
using TestPlans.Contracts;

namespace TestPlans.Application.Reads;

/// <summary>List a test plan's versions.</summary>
public static class ListVersions
{
    public sealed record Query(Guid TestPlanId) : IRequest<IReadOnlyList<VersionSummary>>;

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<VersionSummary>>
    {
        private readonly ITestPlanCatalog _catalog;

        public Handler(ITestPlanCatalog catalog)
        {
            _catalog = catalog;
        }

        public Task<IReadOnlyList<VersionSummary>> Handle(Query query, CancellationToken cancellationToken) =>
            _catalog.GetVersionsAsync(query.TestPlanId, cancellationToken);
    }
}
