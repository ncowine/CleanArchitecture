using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Application.Outbox;
using Students.Domain;

namespace Students.Application.Students;

/// <summary>
/// Withdraw a student. A withdrawn student causes later hold requests to be rejected (the existing saga),
/// and — on the transition to withdrawn — publishes <see cref="StudentWithdrawn"/> so the Library returns
/// their loans and cancels their reservations.
/// </summary>
public static class WithdrawStudent
{
    public sealed record Command(Guid StudentId) : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IStudentRepository _repository;
        private readonly IStudentCacheInvalidator _cache;
        private readonly IStudentOutbox _outbox;

        public Handler(IStudentRepository repository, IStudentCacheInvalidator cache, IStudentOutbox outbox)
        {
            _repository = repository;
            _cache = cache;
            _outbox = outbox;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var student = await _repository.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");

            var wasActive = student.Status != StudentStatus.Withdrawn;
            student.Withdraw();

            // Cascade into the Library, atomically with the withdrawal — but only on the transition, so a
            // repeated withdrawal doesn't re-enqueue.
            if (wasActive)
            {
                _outbox.Enqueue(new StudentWithdrawn(student.Id));
            }

            // Evict the cached summary so reads reflect the new status. (Done here for the POC; strictly
            // this should run after the unit of work commits to fully close a repopulate race — the
            // short cache TTL also self-heals it.)
            await _cache.RemoveAsync(command.StudentId, cancellationToken);

            return student.Id;
        }
    }
}
