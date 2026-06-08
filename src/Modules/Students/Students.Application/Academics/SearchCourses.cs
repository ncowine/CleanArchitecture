using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>Paged catalog search, optionally filtered by department.</summary>
public static class SearchCourses
{
    public sealed record Query(int Page = 1, int PageSize = 20, string? Department = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<CourseListItem>>;

    public sealed record CourseListItem(Guid Id, string Code, string Title, int Credits, string DepartmentName);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<CourseListItem>>
    {
        private readonly ICourseReadService _reads;

        public Handler(ICourseReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<CourseListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.SearchAsync(query.Page, query.PageSize, query.Department, cancellationToken);
    }
}
