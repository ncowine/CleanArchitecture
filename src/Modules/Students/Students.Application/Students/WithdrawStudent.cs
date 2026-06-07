using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Students;

/// <summary>Withdraw a student. A withdrawn student causes later hold requests to be rejected (saga).</summary>
public static class WithdrawStudent
{
    public sealed record Command(Guid StudentId) : IRequest<Guid>, IStudentsCommand;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IStudentRepository _repository;

        public Handler(IStudentRepository repository)
        {
            _repository = repository;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var student = await _repository.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");

            student.Withdraw();
            return student.Id;
        }
    }
}
