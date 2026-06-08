using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Add an instructor to the directory.</summary>
public static class CreateInstructor
{
    public sealed record Command(
        string FirstName, string LastName, string Email, string DepartmentName, InstructorRank Rank)
        : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.FirstName).NotEmpty().MaximumLength(100);
            RuleFor(command => command.LastName).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(command => command.DepartmentName).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Rank).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IInstructorRepository _instructors;

        public Handler(IInstructorRepository instructors)
        {
            _instructors = instructors;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var instructor = Instructor.Create(
                command.FirstName, command.LastName, command.Email, command.DepartmentName, command.Rank);

            await _instructors.AddAsync(instructor, cancellationToken);
            return instructor.Id;
        }
    }
}
