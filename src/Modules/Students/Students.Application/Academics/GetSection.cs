using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>A section with its course, instructor, schedule, capacity, and live enrolled/waitlist counts.</summary>
public static class GetSection
{
    public sealed record Query(Guid SectionId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        Guid CourseId,
        string CourseCode,
        string CourseTitle,
        Guid InstructorId,
        string InstructorName,
        string Term,
        string SectionCode,
        int Capacity,
        int EnrolledCount,
        int WaitlistCount,
        string Status,
        ScheduleDto Schedule);

    public sealed record ScheduleDto(string Days, TimeOnly StartTime, TimeOnly EndTime, string Room);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly ISectionReadService _reads;

        public Handler(ISectionReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.SectionId, cancellationToken);
    }
}
