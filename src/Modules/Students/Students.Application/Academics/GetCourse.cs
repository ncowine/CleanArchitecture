using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>A course with its prerequisite list (each resolved to code + title).</summary>
public static class GetCourse
{
    public sealed record Query(Guid CourseId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string Code,
        string Title,
        string? Description,
        int Credits,
        string DepartmentName,
        IReadOnlyList<PrerequisiteDto> Prerequisites);

    public sealed record PrerequisiteDto(Guid CourseId, string Code, string Title);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly ICourseReadService _reads;

        public Handler(ICourseReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.CourseId, cancellationToken);
    }
}
