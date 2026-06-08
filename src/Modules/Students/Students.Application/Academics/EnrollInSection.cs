using BuildingBlocks.Messaging;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Academics;

/// <summary>
/// Enroll a student in a section. Enforces (in order): the student exists and is eligible, the section and
/// its course exist, the student has satisfied the course's prerequisites, then defers seat-vs-waitlist to
/// the section aggregate. All in one Students-DB transaction.
/// </summary>
public static class EnrollInSection
{
    public sealed record Command(Guid SectionId, Guid StudentId)
        : IRequest<Result>, IStudentsCommand, IAuditableRequest;

    public sealed record Result(Guid EnrollmentId, string Status, int? WaitlistPosition);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly ICourseSectionRepository _sections;
        private readonly ICourseRepository _courses;
        private readonly IStudentRepository _students;
        private readonly ISectionReadService _sectionReads;
        private readonly IAccountCharger _charger;

        public Handler(
            ICourseSectionRepository sections,
            ICourseRepository courses,
            IStudentRepository students,
            ISectionReadService sectionReads,
            IAccountCharger charger)
        {
            _sections = sections;
            _courses = courses;
            _students = students;
            _sectionReads = sectionReads;
            _charger = charger;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var student = await _students.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");
            if (student.Status is StudentStatus.Withdrawn or StudentStatus.Graduated)
                throw new DomainException($"A {student.Status} student cannot enroll in a section.");

            var section = await _sections.GetAsync(command.SectionId, cancellationToken)
                ?? throw new DomainException($"No section exists with id '{command.SectionId}'.");

            var course = await _courses.GetAsync(section.CourseId, cancellationToken)
                ?? throw new DomainException($"No course exists with id '{section.CourseId}'.");

            var required = course.Prerequisites.Select(prerequisite => prerequisite.PrerequisiteCourseId).ToList();
            if (required.Count > 0)
            {
                var satisfied = await _sectionReads.GetSatisfiedCourseIdsAsync(command.StudentId, cancellationToken);
                if (required.Exists(courseId => !satisfied.Contains(courseId)))
                    throw new DomainException("The student has not satisfied all prerequisites for this course.");
            }

            var enrollment = section.Enroll(command.StudentId, DateOnly.FromDateTime(DateTime.UtcNow));

            // Taking a seat incurs tuition (credits × rate); waitlisting does not. Same Students DB, so this
            // charge — and any financial hold it triggers — commits in the same transaction as the enrollment.
            if (enrollment.Status == SectionEnrollmentStatus.Enrolled)
            {
                var tuition = course.Credits * BillingPolicy.TuitionPerCredit;
                await _charger.ChargeAsync(
                    command.StudentId, tuition, ChargeCategory.Tuition, $"Tuition: {course.Code}", cancellationToken);
            }

            return new Result(enrollment.Id, enrollment.Status.ToString(), enrollment.WaitlistPosition);
        }
    }
}
