using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>Declare that one course is a prerequisite of another. Enforced at section enrollment time.</summary>
public static class AddCoursePrerequisite
{
    public sealed record Command(Guid CourseId, Guid PrerequisiteCourseId)
        : IRequest<Guid>, IStudentsCommand, IAuditableRequest;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ICourseRepository _courses;

        public Handler(ICourseRepository courses)
        {
            _courses = courses;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var course = await _courses.GetAsync(command.CourseId, cancellationToken)
                ?? throw new DomainException($"No course exists with id '{command.CourseId}'.");

            if (!await _courses.ExistsAsync(command.PrerequisiteCourseId, cancellationToken))
                throw new DomainException($"No prerequisite course exists with id '{command.PrerequisiteCourseId}'.");

            course.AddPrerequisite(command.PrerequisiteCourseId);
            return course.Id;
        }
    }
}
