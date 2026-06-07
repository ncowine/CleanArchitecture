using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Students;

/// <summary>Reads the holds on a student — where the cross-database outbox write-back lands.</summary>
public static class GetStudentHolds
{
    public sealed record Query(Guid StudentId) : IRequest<IReadOnlyList<Dto>>;

    public sealed record Dto(Guid Id, string Reason, DateTime PlacedOnUtc);

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<Dto>>
    {
        private readonly IStudentRepository _repository;

        public Handler(IStudentRepository repository)
        {
            _repository = repository;
        }

        public async Task<IReadOnlyList<Dto>> Handle(Query query, CancellationToken cancellationToken)
        {
            var holds = await _repository.GetHoldsAsync(query.StudentId, cancellationToken);
            return holds.Select(hold => new Dto(hold.Id, hold.Reason, hold.PlacedOnUtc)).ToList();
        }
    }
}
