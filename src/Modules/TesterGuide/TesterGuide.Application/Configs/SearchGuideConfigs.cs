using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using TesterGuide.Application.Abstractions;

namespace TesterGuide.Application.Configs;

/// <summary>Paged search of guide configs, optionally filtered by test plan (paging/filters in the body).</summary>
public static class SearchGuideConfigs
{
    public sealed record ConfigListItem(
        Guid Id, string Name, Guid TestPlanId, Guid TestPlanVersionId, string Mode, bool SyncEnabled, string Status);

    public sealed record Query(int Page = 1, int PageSize = 20, Guid? TestPlanId = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<ConfigListItem>>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<ConfigListItem>>
    {
        private readonly IGuideReadService _reads;

        public Handler(IGuideReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<ConfigListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.SearchConfigsAsync(query.Page, query.PageSize, query.TestPlanId, cancellationToken);
    }
}
