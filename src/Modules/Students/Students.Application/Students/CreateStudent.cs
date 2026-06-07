using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Students;

/// <summary>Enroll a new student. Keeps a real validator — these rules go beyond the domain guards.</summary>
public static class CreateStudent
{
    public sealed record Command(
        string FirstName,
        string LastName,
        string Email,
        DateOnly DateOfBirth,
        DateOnly EnrolledOn) : IRequest<Guid>, IStudentsCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.FirstName).NotEmpty().MaximumLength(100);
            RuleFor(command => command.LastName).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(command => command.DateOfBirth)
                .NotEqual(default(DateOnly)).WithMessage("Date of birth is required.")
                .LessThan(command => command.EnrolledOn).WithMessage("Date of birth must be before the enrollment date.");
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IStudentRepository _repository;

        public Handler(IStudentRepository repository)
        {
            _repository = repository;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var student = Student.Create(
                firstName: command.FirstName,
                lastName: command.LastName,
                email: command.Email,
                dateOfBirth: command.DateOfBirth,
                enrolledOn: command.EnrolledOn);

            await _repository.AddAsync(student, cancellationToken);
            return student.Id;
        }
    }
}
