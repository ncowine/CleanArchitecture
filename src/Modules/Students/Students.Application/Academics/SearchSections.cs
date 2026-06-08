using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>Paged section search, optionally filtered by term, course, and/or instructor.</summary>
public static class SearchSections
{
    public sealed record Query(
        int Page = 1, int PageSize = 20, string? Term = null, Guid? CourseId = null, Guid? InstructorId = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<SectionListItem>>;

    public sealed record SectionListItem(
        Guid Id,
        string CourseCode,
        string CourseTitle,
        string Term,
        string SectionCode,
        string InstructorName,
        int Capacity,
        int EnrolledCount,
        string Status);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<SectionListItem>>
    {
        private readonly ISectionReadService _reads;

        public Handler(ISectionReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<SectionListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.SearchAsync(
                query.Page, query.PageSize, query.Term, query.CourseId, query.InstructorId, cancellationToken);
    }
}
