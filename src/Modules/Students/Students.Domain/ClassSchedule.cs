namespace Students.Domain;

/// <summary>
/// When and where a section meets. A value object owned by <see cref="CourseSection"/> (stored as columns
/// on the section's table).
/// </summary>
public sealed class ClassSchedule
{
    public MeetingDays Days { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public string Room { get; private set; } = null!;

    private ClassSchedule() { }

    private ClassSchedule(MeetingDays days, TimeOnly startTime, TimeOnly endTime, string room)
    {
        Days = days;
        StartTime = startTime;
        EndTime = endTime;
        Room = room;
    }

    public static ClassSchedule Create(MeetingDays days, TimeOnly startTime, TimeOnly endTime, string room)
    {
        if (days == MeetingDays.None)
            throw new DomainException("A section must meet on at least one day.");
        if (endTime <= startTime)
            throw new DomainException("The end time must be after the start time.");
        if (string.IsNullOrWhiteSpace(room))
            throw new DomainException("A room is required.");

        return new ClassSchedule(days, startTime, endTime, room.Trim());
    }
}
