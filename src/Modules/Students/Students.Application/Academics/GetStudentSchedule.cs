using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>
/// A student's class schedule for a term — the sections they're actively enrolled in. Inherently small
/// (a handful of courses), so it's returned unpaged.
/// </summary>
public static class GetStudentSchedule
{
    public sealed record Query(Guid StudentId, string Term) : IRequest<IReadOnlyList<ScheduledSection>>;

    public sealed record ScheduledSection(
        Guid SectionId,
        string CourseCode,
        string CourseTitle,
        string SectionCode,
        string InstructorName,
        string Status,
        string Days,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Room);

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<ScheduledSection>>
    {
        private readonly ISectionReadService _reads;

        public Handler(ISectionReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<ScheduledSection>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetStudentScheduleAsync(query.StudentId, query.Term, cancellationToken);
    }
}
