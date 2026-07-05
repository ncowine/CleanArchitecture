using TesterGuide.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class FocusTests
{
    [Fact]
    public void Create_trims_and_defaults_null_description()
    {
        var focus = Focus.Create("  Performance  ", null);

        Assert.Equal("Performance", focus.Name);
        Assert.Equal(string.Empty, focus.Description);
    }

    [Fact]
    public void Create_with_empty_name_throws() =>
        Assert.Throws<DomainException>(() => Focus.Create("  ", "desc"));

    [Fact]
    public void Rename_updates_name_and_description()
    {
        var focus = Focus.Create("Old", "old desc");

        focus.Rename("New", "new desc");

        Assert.Equal("New", focus.Name);
        Assert.Equal("new desc", focus.Description);
    }

    [Fact]
    public void ContentSelection_requires_plan_and_task()
    {
        Assert.Throws<DomainException>(() => ContentSelection.Create(Guid.Empty, Guid.NewGuid(), true));
        Assert.Throws<DomainException>(() => ContentSelection.Create(Guid.NewGuid(), Guid.Empty, true));
    }

    [Fact]
    public void ContentSelection_toggles_enabled()
    {
        var selection = ContentSelection.Create(Guid.NewGuid(), Guid.NewGuid(), true);
        Assert.True(selection.IsEnabled);

        selection.SetEnabled(false);
        Assert.False(selection.IsEnabled);
    }
}
