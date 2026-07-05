using TesterGuide.Application.Actions;
using TesterGuide.Application.Outbox;
using TesterGuide.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class RecordActionHandlerTests
{
    private static GuideConfig ConfigWith(bool syncEnabled)
    {
        var config = GuideConfig.Create(
            "run", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GuideMode.SinglePlayer, syncEnabled, "ada");
        return config;
    }

    private static RecordAction.Handler HandlerFor(
        FakeGuideConfigRepository configs,
        FakeGuideActionLogRepository actions,
        FakeTesterGuideOutbox outbox,
        FakeRealtimeDispatch? realtime = null) =>
        new(configs, actions, outbox, realtime ?? new FakeRealtimeDispatch(), new FakeCurrentActor("ada"));

    [Fact]
    public async Task Sync_enabled_records_pending_and_enqueues_the_forward_event()
    {
        var config = ConfigWith(syncEnabled: true);
        var configs = new FakeGuideConfigRepository();
        configs.SeedConfig(config);
        var actions = new FakeGuideActionLogRepository();
        var outbox = new FakeTesterGuideOutbox();
        var realtime = new FakeRealtimeDispatch();
        var handler = HandlerFor(configs, actions, outbox, realtime);

        var result = await handler.Handle(
            new RecordAction.Command(config.Id, Guid.NewGuid(), Guid.NewGuid(), ActionStatus.Pass), default);

        var entry = Assert.Single(actions.Added);
        Assert.Equal(SyncState.Pending, entry.SyncState);
        Assert.Equal("Pending", result.SyncState);

        var evt = Assert.Single(outbox.Events);
        var requested = Assert.IsType<MainDbActionRequested>(evt);
        Assert.Equal(entry.Id, requested.GuideActionId); // source reference ties the saga legs
        Assert.Equal("Pass", requested.Status);

        // Every action broadcasts to the config's realtime group ("someone actioned this").
        var (group, published) = Assert.Single(realtime.Published);
        Assert.Equal($"config:{config.Id}", group);
        Assert.Equal("TaskActioned", published.Type);
    }

    [Fact]
    public async Task Sync_disabled_records_not_synced_and_enqueues_nothing()
    {
        var config = ConfigWith(syncEnabled: false);
        var configs = new FakeGuideConfigRepository();
        configs.SeedConfig(config);
        var actions = new FakeGuideActionLogRepository();
        var outbox = new FakeTesterGuideOutbox();
        var handler = HandlerFor(configs, actions, outbox);

        var result = await handler.Handle(
            new RecordAction.Command(config.Id, Guid.NewGuid(), Guid.NewGuid(), ActionStatus.Skip), default);

        var entry = Assert.Single(actions.Added);
        Assert.Equal(SyncState.NotSynced, entry.SyncState);
        Assert.Equal("NotSynced", result.SyncState);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Missing_config_throws_and_records_nothing()
    {
        var configs = new FakeGuideConfigRepository(); // empty
        var actions = new FakeGuideActionLogRepository();
        var outbox = new FakeTesterGuideOutbox();
        var handler = HandlerFor(configs, actions, outbox);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new RecordAction.Command(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ActionStatus.Pass), default));

        Assert.Empty(actions.Added);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task A_closed_config_cannot_be_actioned()
    {
        var config = ConfigWith(syncEnabled: true);
        config.Close();
        var configs = new FakeGuideConfigRepository();
        configs.SeedConfig(config);
        var handler = HandlerFor(configs, new FakeGuideActionLogRepository(), new FakeTesterGuideOutbox());

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new RecordAction.Command(config.Id, Guid.NewGuid(), Guid.NewGuid(), ActionStatus.Pass), default));
    }
}
