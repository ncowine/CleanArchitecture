namespace Students.Domain;

/// <summary>
/// A student's enrollment in an <see cref="AcademicProgram"/> for a given term. Part of the
/// <see cref="Student"/> aggregate. References the program by id only — <c>AcademicProgram</c> is a
/// separate aggregate, so we never hold a navigation across that boundary.
/// </summary>
public sealed class Enrollment
{
    public Guid Id { get; private set; }
    public Guid ProgramId { get; private set; }
    public string Term { get; private set; } = null!;
    public DateOnly EnrolledOn { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public string? Grade { get; private set; }

    private Enrollment() { }

    private Enrollment(Guid id, Guid programId, string term, DateOnly enrolledOn)
    {
        Id = id;
        ProgramId = programId;
        Term = term;
        EnrolledOn = enrolledOn;
        Status = EnrollmentStatus.Enrolled;
    }

    internal static Enrollment Create(Guid programId, string term, DateOnly enrolledOn)
    {
        if (programId == Guid.Empty)
            throw new DomainException("A program is required to enroll.");
        if (string.IsNullOrWhiteSpace(term))
            throw new DomainException("Term is required.");

        return new Enrollment(Guid.NewGuid(), programId, term.Trim(), enrolledOn);
    }

    internal void Complete(string grade)
    {
        if (Status != EnrollmentStatus.Enrolled)
            throw new DomainException("Only an active enrollment can be completed.");
        if (string.IsNullOrWhiteSpace(grade))
            throw new DomainException("A grade is required to complete an enrollment.");

        Grade = grade.Trim();
        Status = EnrollmentStatus.Completed;
    }

    internal void Drop()
    {
        if (Status != EnrollmentStatus.Enrolled)
            throw new DomainException("Only an active enrollment can be dropped.");

        Status = EnrollmentStatus.Dropped;
    }
}
