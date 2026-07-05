using TesterGuide.Application.Configs;
using TesterGuide.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class CreateGuideConfigHandlerTests
{
    private static CreateGuideConfig.Command CommandFor(Guid focusId) =>
        new("Perf run", Guid.NewGuid(), Guid.NewGuid(), focusId, GuideMode.Multiplayer, false);

    [Fact]
    public async Task Valid_input_creates_a_config_with_the_current_actor()
    {
        var focuses = new FakeFocusRepository();
        var focus = Focus.Create("Perf", null);
        focuses.Seed(focus);
        var configs = new FakeGuideConfigRepository();
        var catalog = new FakeTestPlanCatalog { VersionExists = true };
        var handler = new CreateGuideConfig.Handler(configs, focuses, catalog, new FakeCurrentActor("ada"));

        var id = await handler.Handle(CommandFor(focus.Id), default);

        var added = Assert.Single(configs.Added);
        Assert.Equal(id, added.Id);
        Assert.Equal("ada", added.CreatedBy);
        Assert.Equal(ConfigStatus.Draft, added.Status);
    }

    [Fact]
    public async Task Missing_focus_throws_and_writes_no_config()
    {
        var focuses = new FakeFocusRepository(); // empty
        var configs = new FakeGuideConfigRepository();
        var catalog = new FakeTestPlanCatalog { VersionExists = true };
        var handler = new CreateGuideConfig.Handler(configs, focuses, catalog, new FakeCurrentActor("ada"));

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(CommandFor(Guid.NewGuid()), default));

        Assert.Empty(configs.Added);
    }

    [Fact]
    public async Task Missing_version_in_primary_system_throws_and_writes_no_config()
    {
        var focuses = new FakeFocusRepository();
        var focus = Focus.Create("Perf", null);
        focuses.Seed(focus);
        var configs = new FakeGuideConfigRepository();
        var catalog = new FakeTestPlanCatalog { VersionExists = false }; // cross-module check fails
        var handler = new CreateGuideConfig.Handler(configs, focuses, catalog, new FakeCurrentActor("ada"));

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(CommandFor(focus.Id), default));

        Assert.Empty(configs.Added);
    }
}
