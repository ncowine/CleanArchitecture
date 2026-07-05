using BuildingBlocks.RealTime;
using Xunit;

namespace CleanArch.UnitTests;

public class PresenceTrackerTests
{
    [Fact]
    public void Tracks_users_in_a_group_and_removes_them_on_leave()
    {
        var tracker = new InMemoryPresenceTracker();

        tracker.Join("config:1", "conn-a", "ada");
        tracker.Join("config:1", "conn-b", "grace");

        Assert.Equal(new[] { "ada", "grace" }, tracker.UsersIn("config:1").OrderBy(u => u));

        tracker.Leave("conn-a");

        Assert.Equal(new[] { "grace" }, tracker.UsersIn("config:1"));
    }

    [Fact]
    public void The_same_user_on_two_connections_appears_once()
    {
        var tracker = new InMemoryPresenceTracker();

        tracker.Join("config:1", "conn-a", "ada");
        tracker.Join("config:1", "conn-b", "ada");

        Assert.Equal(new[] { "ada" }, tracker.UsersIn("config:1"));
    }

    [Fact]
    public void Leave_clears_the_connection_from_every_group_it_joined()
    {
        var tracker = new InMemoryPresenceTracker();
        tracker.Join("config:1", "conn-a", "ada");
        tracker.Join("config:2", "conn-a", "ada");

        Assert.Equal(new[] { "config:1", "config:2" }, tracker.GroupsFor("conn-a").OrderBy(g => g));

        tracker.Leave("conn-a");

        Assert.Empty(tracker.UsersIn("config:1"));
        Assert.Empty(tracker.UsersIn("config:2"));
        Assert.Empty(tracker.GroupsFor("conn-a"));
    }

    [Fact]
    public void Unknown_group_and_connection_return_empty()
    {
        var tracker = new InMemoryPresenceTracker();

        Assert.Empty(tracker.UsersIn("nope"));
        Assert.Empty(tracker.GroupsFor("nobody"));
    }
}
