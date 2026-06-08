using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;

namespace Students.Application.Students;

/// <summary>Reads a page of the holds on a student — where the cross-database outbox write-back lands.</summary>
public static class GetStudentHolds
{
    public sealed record Query(Guid StudentId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<Dto>>;

    public sealed record Dto(Guid Id, string Reason, DateTime PlacedOnUtc);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);

            // Cap the page size — never let a caller ask for an unbounded page.
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<Dto>>
    {
        private readonly IStudentRepository _repository;

        public Handler(IStudentRepository repository)
        {
            _repository = repository;
        }

        public async Task<PagedResult<Dto>> Handle(Query query, CancellationToken cancellationToken)
        {
            var holds = await _repository.GetHoldsAsync(query.StudentId, query.Page, query.PageSize, cancellationToken);

            var items = holds.Items
                .Select(hold => new Dto(hold.Id, hold.Reason, hold.PlacedOnUtc))
                .ToList();

            return new PagedResult<Dto>(items, holds.Page, holds.PageSize, holds.TotalCount);
        }
    }
}
