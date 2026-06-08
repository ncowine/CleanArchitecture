using BuildingBlocks.Messaging;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Open a section of a course for a term, taught by an instructor, with a capacity and schedule.</summary>
public static class ScheduleSection
{
    public sealed record Command(
        Guid CourseId,
        Guid InstructorId,
        string Term,
        string SectionCode,
        int Capacity,
        MeetingDays Days,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string Room) : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.CourseId).NotEmpty();
            RuleFor(command => command.InstructorId).NotEmpty();
            RuleFor(command => command.Term).NotEmpty().MaximumLength(50);
            RuleFor(command => command.SectionCode).NotEmpty().MaximumLength(20);
            RuleFor(command => command.Capacity).InclusiveBetween(1, 1000);
            RuleFor(command => command.Days).NotEqual(MeetingDays.None).WithMessage("Select at least one meeting day.");
            RuleFor(command => command.Room).NotEmpty().MaximumLength(50);
            RuleFor(command => command.EndTime).GreaterThan(command => command.StartTime);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseRepository _courses;
        private readonly IInstructorRepository _instructors;

        public Handler(
            ICourseSectionRepository sections, ICourseRepository courses, IInstructorRepository instructors)
        {
            _sections = sections;
            _courses = courses;
            _instructors = instructors;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _courses.ExistsAsync(command.CourseId, cancellationToken))
                throw new DomainException($"No course exists with id '{command.CourseId}'.");
            if (!await _instructors.ExistsAsync(command.InstructorId, cancellationToken))
                throw new DomainException($"No instructor exists with id '{command.InstructorId}'.");

            var schedule = ClassSchedule.Create(command.Days, command.StartTime, command.EndTime, command.Room);
            var section = CourseSection.Create(
                command.CourseId, command.InstructorId, command.Term, command.SectionCode, command.Capacity, schedule);

            await _sections.AddAsync(section, cancellationToken);
            return section.Id;
        }
    }
}
