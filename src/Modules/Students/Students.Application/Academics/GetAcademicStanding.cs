using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Academics;

/// <summary>A student's GPA summary and derived academic standing — the light projection of the transcript.</summary>
public static class GetAcademicStanding
{
    public sealed record Query(Guid StudentId) : IRequest<Response>;

    public sealed record Response(
        Guid StudentId, decimal CumulativeGpa, int EarnedCredits, int AttemptedCredits, string Standing);

    public sealed class Handler : IRequestHandler<Query, Response>
    {
        private readonly ITranscriptReadService _reads;

        public Handler(ITranscriptReadService reads)
        {
            _reads = reads;
        }

        public Task<Response> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetStandingAsync(query.StudentId, cancellationToken);
    }
}
