using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Billing;

/// <summary>Post a charge (tuition, fee, fine, …) to a student's account, opening the account on first use.</summary>
public static class ChargeAccount
{
    public sealed record Command(Guid StudentId, decimal Amount, ChargeCategory Category, string Description)
        : IRequest<Result>, IStudentsCommand, IAuditableRequest;

    public sealed record Result(decimal Balance);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.StudentId).NotEmpty();
            RuleFor(command => command.Amount).GreaterThan(0m);
            RuleFor(command => command.Category).IsInEnum();
            RuleFor(command => command.Description).NotEmpty().MaximumLength(200);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IAccountCharger _charger;
        private readonly IStudentRepository _students;

        public Handler(IAccountCharger charger, IStudentRepository students)
        {
            _charger = charger;
            _students = students;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            if (await _students.GetAsync(command.StudentId, cancellationToken) is null)
                throw new DomainException($"No student exists with id '{command.StudentId}'.");

            // Charging also places a financial hold if it pushes the balance over the limit.
            var balance = await _charger.ChargeAsync(
                command.StudentId, command.Amount, command.Category, command.Description, cancellationToken);

            return new Result(balance);
        }
    }
}
