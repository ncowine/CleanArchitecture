using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>Paged roster of a section — enrolled and waitlisted students, with their student names resolved.</summary>
public static class GetSectionRoster
{
    public sealed record Query(Guid SectionId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<RosterEntry>>;

    public sealed record RosterEntry(
        Guid StudentId, string StudentName, string Status, int? WaitlistPosition, DateOnly EnrolledOn);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<RosterEntry>>
    {
        private readonly ISectionReadService _reads;

        public Handler(ISectionReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<RosterEntry>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetRosterAsync(query.SectionId, query.Page, query.PageSize, cancellationToken);
    }
}
