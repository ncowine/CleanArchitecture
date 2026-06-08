namespace Students.Domain;

/// <summary>
/// A student's place in a <see cref="CourseSection"/> — enrolled, waitlisted, dropped, or completed. Part
/// of the section aggregate; references the student by id (the <see cref="Student"/> is a separate
/// aggregate). Mutated only through its owning <see cref="CourseSection"/>, so the API is <c>internal</c>.
/// </summary>
public sealed class SectionEnrollment
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public DateOnly EnrolledOn { get; private set; }
    public SectionEnrollmentStatus Status { get; private set; }

    /// <summary>1-based position while <see cref="SectionEnrollmentStatus.Waitlisted"/>; null otherwise.</summary>
    public int? WaitlistPosition { get; private set; }

    /// <summary>The grade earned; null until the enrollment is graded (which completes it).</summary>
    public Grade? Grade { get; private set; }

    private SectionEnrollment() { }

    internal SectionEnrollment(Guid studentId, DateOnly enrolledOn, SectionEnrollmentStatus status, int? waitlistPosition)
    {
        Id = Guid.NewGuid();
        StudentId = studentId;
        EnrolledOn = enrolledOn;
        Status = status;
        WaitlistPosition = waitlistPosition;
    }

    internal void Promote()
    {
        Status = SectionEnrollmentStatus.Enrolled;
        WaitlistPosition = null;
    }

    internal void Drop()
    {
        Status = SectionEnrollmentStatus.Dropped;
        WaitlistPosition = null;
    }

    /// <summary>Records the grade and completes the enrollment. Only an enrolled student can be graded.</summary>
    internal void RecordGrade(Grade grade)
    {
        if (Status != SectionEnrollmentStatus.Enrolled)
            throw new DomainException("Only an enrolled student can be graded.");

        Grade = grade;
        Status = SectionEnrollmentStatus.Completed;
        WaitlistPosition = null;
    }

    internal void SetWaitlistPosition(int position) => WaitlistPosition = position;
}
