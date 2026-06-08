using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class ClassScheduleTests
{
    private static readonly TimeOnly Ten = new(10, 0);
    private static readonly TimeOnly Eleven = new(11, 0);

    [Fact]
    public void Create_with_valid_input_succeeds()
    {
        var schedule = ClassSchedule.Create(MeetingDays.Monday | MeetingDays.Friday, Ten, Eleven, " Room 5 ");

        Assert.Equal(MeetingDays.Monday | MeetingDays.Friday, schedule.Days);
        Assert.Equal("Room 5", schedule.Room);
    }

    [Fact]
    public void Create_with_no_days_throws() =>
        Assert.Throws<DomainException>(() => ClassSchedule.Create(MeetingDays.None, Ten, Eleven, "Room 5"));

    [Fact]
    public void Create_with_end_not_after_start_throws() =>
        Assert.Throws<DomainException>(() => ClassSchedule.Create(MeetingDays.Monday, Eleven, Ten, "Room 5"));

    [Fact]
    public void Create_with_empty_room_throws() =>
        Assert.Throws<DomainException>(() => ClassSchedule.Create(MeetingDays.Monday, Ten, Eleven, "  "));
}
