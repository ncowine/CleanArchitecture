using BuildingBlocks.Messaging;
using TestPlans.Contracts;

namespace TestPlans.Application.Reads;

/// <summary>Read a test plan's full content tree (categories → sub-categories → tasks).</summary>
public static class GetTestPlanTree
{
    public sealed record Query(Guid TestPlanId) : IRequest<TestPlanTree?>;

    public sealed class Handler : IRequestHandler<Query, TestPlanTree?>
    {
        private readonly ITestPlanCatalog _catalog;

        public Handler(ITestPlanCatalog catalog)
        {
            _catalog = catalog;
        }

        public Task<TestPlanTree?> Handle(Query query, CancellationToken cancellationToken) =>
            _catalog.GetTreeAsync(query.TestPlanId, cancellationToken);
    }
}
