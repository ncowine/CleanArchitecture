using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Record a letter grade for an enrolled student, completing their section enrollment.</summary>
public static class RecordGrade
{
    public sealed record Command(Guid SectionId, Guid StudentId, string Grade)
        : IRequest<Result>, IStudentsCommand, IAuditableRequest;

    public sealed record Result(Guid StudentId, string Grade, decimal Points);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.SectionId).NotEmpty();
            RuleFor(command => command.StudentId).NotEmpty();
            RuleFor(command => command.Grade).NotEmpty().MaximumLength(2);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly ICourseSectionRepository _sections;

        public Handler(ICourseSectionRepository sections)
        {
            _sections = sections;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var section = await _sections.GetAsync(command.SectionId, cancellationToken)
                ?? throw new DomainException($"No section exists with id '{command.SectionId}'.");

            // Validates the letter against the 4.0 scale and throws DomainException if unknown.
            var grade = Domain.Grade.FromLetter(command.Grade);
            section.RecordGrade(command.StudentId, grade);

            return new Result(command.StudentId, grade.Letter, grade.Points);
        }
    }
}
