namespace Students.Domain;

/// <summary>
/// A specific offering of a <see cref="Course"/> in a term, taught by an <see cref="Instructor"/>, with a
/// capacity and a roster. Aggregate root — references the course and instructor by id only. Owns the
/// enrollment roster, including the waitlist that fills automatically when capacity is reached and drains
/// as seats free up.
/// </summary>
public sealed class CourseSection
{
    private readonly List<SectionEnrollment> _roster = [];

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid InstructorId { get; private set; }
    public string Term { get; private set; } = null!;
    public string SectionCode { get; private set; } = null!;
    public int Capacity { get; private set; }
    public ClassSchedule Schedule { get; private set; } = null!;
    public SectionStatus Status { get; private set; }

    public IReadOnlyCollection<SectionEnrollment> Roster => _roster.AsReadOnly();

    public int EnrolledCount => _roster.Count(enrollment => enrollment.Status == SectionEnrollmentStatus.Enrolled);
    public int WaitlistCount => _roster.Count(enrollment => enrollment.Status == SectionEnrollmentStatus.Waitlisted);

    private CourseSection() { }

    private CourseSection(
        Guid id, Guid courseId, Guid instructorId, string term, string sectionCode, int capacity, ClassSchedule schedule)
    {
        Id = id;
        CourseId = courseId;
        InstructorId = instructorId;
        Term = term;
        SectionCode = sectionCode;
        Capacity = capacity;
        Schedule = schedule;
        Status = SectionStatus.Open;
    }

    public static CourseSection Create(
        Guid courseId, Guid instructorId, string term, string sectionCode, int capacity, ClassSchedule schedule)
    {
        if (courseId == Guid.Empty)
            throw new DomainException("A course is required.");
        if (instructorId == Guid.Empty)
            throw new DomainException("An instructor is required.");
        if (string.IsNullOrWhiteSpace(term))
            throw new DomainException("Term is required.");
        if (string.IsNullOrWhiteSpace(sectionCode))
            throw new DomainException("Section code is required.");
        if (capacity < 1)
            throw new DomainException("Capacity must be at least 1.");
        ArgumentNullException.ThrowIfNull(schedule);

        return new CourseSection(
            id: Guid.NewGuid(),
            courseId: courseId,
            instructorId: instructorId,
            term: term.Trim(),
            sectionCode: sectionCode.Trim().ToUpperInvariant(),
            capacity: capacity,
            schedule: schedule);
    }

    /// <summary>
    /// Enrolls a student. Takes a seat if one is free, otherwise appends them to the waitlist. Returns the
    /// resulting roster entry so the caller can tell the student whether they're enrolled or waitlisted.
    /// </summary>
    public SectionEnrollment Enroll(Guid studentId, DateOnly enrolledOn)
    {
        if (studentId == Guid.Empty)
            throw new DomainException("A student is required.");
        if (Status != SectionStatus.Open)
            throw new DomainException($"Cannot enroll in a {Status} section.");
        if (_roster.Any(enrollment => enrollment.StudentId == studentId
                && enrollment.Status is SectionEnrollmentStatus.Enrolled or SectionEnrollmentStatus.Waitlisted))
            throw new DomainException("The student is already enrolled in or waitlisted for this section.");

        var enrollment = EnrolledCount < Capacity
            ? new SectionEnrollment(studentId, enrolledOn, SectionEnrollmentStatus.Enrolled, waitlistPosition: null)
            : new SectionEnrollment(studentId, enrolledOn, SectionEnrollmentStatus.Waitlisted, WaitlistCount + 1);

        _roster.Add(enrollment);
        return enrollment;
    }

    /// <summary>
    /// Drops a student. If they held a seat, the head of the waitlist is promoted into it. The waitlist is
    /// then renumbered so positions stay contiguous (1-based).
    /// </summary>
    public void Drop(Guid studentId)
    {
        var enrollment = _roster.FirstOrDefault(e => e.StudentId == studentId
                && e.Status is SectionEnrollmentStatus.Enrolled or SectionEnrollmentStatus.Waitlisted)
            ?? throw new DomainException("The student is not enrolled in this section.");

        var freedSeat = enrollment.Status == SectionEnrollmentStatus.Enrolled;
        enrollment.Drop();

        if (freedSeat)
        {
            PromoteFromWaitlist();
        }

        RenumberWaitlist();
    }

    public void Cancel() => Status = SectionStatus.Cancelled;

    /// <summary>Records a grade for an enrolled student, completing their enrollment.</summary>
    public void RecordGrade(Guid studentId, Grade grade)
    {
        ArgumentNullException.ThrowIfNull(grade);

        var enrollment = _roster.FirstOrDefault(e =>
                e.StudentId == studentId && e.Status == SectionEnrollmentStatus.Enrolled)
            ?? throw new DomainException("The student is not currently enrolled in this section.");

        enrollment.RecordGrade(grade);
    }

    private void PromoteFromWaitlist()
    {
        if (EnrolledCount >= Capacity)
        {
            return;
        }

        _roster.Where(e => e.Status == SectionEnrollmentStatus.Waitlisted)
            .OrderBy(e => e.WaitlistPosition)
            .FirstOrDefault()
            ?.Promote();
    }

    private void RenumberWaitlist()
    {
        var waitlisted = _roster.Where(e => e.Status == SectionEnrollmentStatus.Waitlisted)
            .OrderBy(e => e.WaitlistPosition)
            .ToList();

        for (var index = 0; index < waitlisted.Count; index++)
        {
            waitlisted[index].SetWaitlistPosition(index + 1);
        }
    }
}
