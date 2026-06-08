using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Billing;

/// <summary>Record a payment against a student's account, reducing the balance owed.</summary>
public static class RecordPayment
{
    public sealed record Command(Guid StudentId, decimal Amount, string? Description)
        : IRequest<Result>, IStudentsCommand, IAuditableRequest;

    public sealed record Result(Guid EntryId, decimal Balance);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.StudentId).NotEmpty();
            RuleFor(command => command.Amount).GreaterThan(0m);
            RuleFor(command => command.Description).MaximumLength(200);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IStudentAccountRepository _accounts;

        public Handler(IStudentAccountRepository accounts)
        {
            _accounts = accounts;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var account = await _accounts.GetByStudentAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No account exists for student '{command.StudentId}'.");

            var description = string.IsNullOrWhiteSpace(command.Description) ? "Payment received" : command.Description;
            var entry = account.RecordPayment(command.Amount, description, DateOnly.FromDateTime(DateTime.UtcNow));

            return new Result(entry.Id, account.Balance);
        }
    }
}
