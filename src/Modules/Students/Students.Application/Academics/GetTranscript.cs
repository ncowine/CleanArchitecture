using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>
/// A student's transcript: every graded (completed) course with its grade, plus the cumulative GPA,
/// earned/attempted credits, and academic standing.
/// </summary>
public static class GetTranscript
{
    public sealed record Query(Guid StudentId) : IRequest<Response>;

    public sealed record Response(
        Guid StudentId,
        IReadOnlyList<TranscriptEntry> Entries,
        decimal CumulativeGpa,
        int EarnedCredits,
        int AttemptedCredits,
        string Standing);

    public sealed record TranscriptEntry(
        string CourseCode, string CourseTitle, int Credits, string Term, string Grade, decimal GradePoints);

    public sealed class Handler : IRequestHandler<Query, Response>
    {
        private readonly ITranscriptReadService _reads;

        public Handler(ITranscriptReadService reads)
        {
            _reads = reads;
        }

        public Task<Response> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetTranscriptAsync(query.StudentId, cancellationToken);
    }
}
