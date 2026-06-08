namespace Students.Domain;

/// <summary>
/// The day(s) of the week a section meets. <c>[Flags]</c> so one section can meet on several days
/// (e.g. <c>Monday | Wednesday | Friday</c>).
/// </summary>
[Flags]
public enum MeetingDays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
}
