using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Add a course to the catalog.</summary>
public static class CreateCourse
{
    public sealed record Command(string Code, string Title, string? Description, int Credits, string DepartmentName)
        : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
            RuleFor(command => command.Title).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Description).MaximumLength(2000);
            RuleFor(command => command.Credits).InclusiveBetween(1, 12);
            RuleFor(command => command.DepartmentName).NotEmpty().MaximumLength(200);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ICourseRepository _courses;

        public Handler(ICourseRepository courses)
        {
            _courses = courses;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var course = Course.Create(
                command.Code, command.Title, command.Description, command.Credits, command.DepartmentName);

            await _courses.AddAsync(course, cancellationToken);
            return course.Id;
        }
    }
}
