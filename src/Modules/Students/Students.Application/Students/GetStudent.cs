using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Students;

/// <summary>
/// Light read — a student's summary. Few fields, no related data; the read service projects only the
/// columns this shape needs (no Includes), so it doesn't pay for the rich graph.
/// </summary>
public static class GetStudent
{
    public sealed record Query(Guid StudentId) : IRequest<Response?>;

    public sealed record Response(Guid Id, string FullName, string Email, string Status);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IStudentReadService _reads;

        public Handler(IStudentReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetSummaryAsync(query.StudentId, cancellationToken);
    }
}
