using TesterGuide.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class GuideConfigTests
{
    private static GuideConfig Valid() =>
        GuideConfig.Create("Perf run", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GuideMode.SinglePlayer, false, "ada");

    [Fact]
    public void Create_with_valid_input_starts_in_draft()
    {
        var config = Valid();

        Assert.Equal(ConfigStatus.Draft, config.Status);
        Assert.Equal("Perf run", config.Name);
        Assert.Equal("ada", config.CreatedBy);
    }

    [Fact]
    public void Create_with_empty_name_throws() =>
        Assert.Throws<DomainException>(() =>
            GuideConfig.Create(" ", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GuideMode.SinglePlayer, false, "ada"));

    [Fact]
    public void Create_without_test_plan_throws() =>
        Assert.Throws<DomainException>(() =>
            GuideConfig.Create("n", Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), GuideMode.SinglePlayer, false, "ada"));

    [Fact]
    public void Create_without_version_throws() =>
        Assert.Throws<DomainException>(() =>
            GuideConfig.Create("n", Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), GuideMode.SinglePlayer, false, "ada"));

    [Fact]
    public void Create_without_focus_throws() =>
        Assert.Throws<DomainException>(() =>
            GuideConfig.Create("n", Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, GuideMode.SinglePlayer, false, "ada"));

    [Fact]
    public void Create_without_creator_throws() =>
        Assert.Throws<DomainException>(() =>
            GuideConfig.Create("n", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GuideMode.SinglePlayer, false, " "));

    [Fact]
    public void Activate_then_close_transitions_status()
    {
        var config = Valid();

        config.Activate();
        Assert.Equal(ConfigStatus.Active, config.Status);

        config.Close();
        Assert.Equal(ConfigStatus.Closed, config.Status);
    }

    [Fact]
    public void A_closed_config_cannot_be_reactivated()
    {
        var config = Valid();
        config.Close();

        Assert.Throws<DomainException>(() => config.Activate());
    }
}
