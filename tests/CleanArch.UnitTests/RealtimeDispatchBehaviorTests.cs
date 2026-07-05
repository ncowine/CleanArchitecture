using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArch.UnitTests;

public class RealtimeDispatchBehaviorTests
{
    private sealed record Ping : IRequest<string>;

    private sealed class RecordingNotifier : IRealtimeNotifier
    {
        public List<(string Group, RealtimeEvent Event)> Sent { get; } = new();

        public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken)
        {
            Sent.Add((group, realtimeEvent));
            return Task.CompletedTask;
        }
    }

    private static RealtimeDispatchBehavior<Ping, string> BehaviorFor(RealtimeDispatch dispatch, RecordingNotifier notifier) =>
        new(dispatch, notifier, NullLogger<RealtimeDispatchBehavior<Ping, string>>.Instance);

    [Fact]
    public async Task Flushes_collected_events_after_a_successful_pipeline()
    {
        var dispatch = new RealtimeDispatch();
        var notifier = new RecordingNotifier();
        var behavior = BehaviorFor(dispatch, notifier);

        // The handler publishes during the inner pipeline, then it completes successfully.
        var response = await behavior.Handle(new Ping(), () =>
        {
            dispatch.Publish("config:1", new RealtimeEvent("TaskActioned", new { a = 1 }));
            return Task.FromResult("ok");
        }, default);

        Assert.Equal("ok", response);
        var (group, evt) = Assert.Single(notifier.Sent);
        Assert.Equal("config:1", group);
        Assert.Equal("TaskActioned", evt.Type);
    }

    [Fact]
    public async Task Publishes_nothing_when_the_pipeline_throws()
    {
        var dispatch = new RealtimeDispatch();
        var notifier = new RecordingNotifier();
        var behavior = BehaviorFor(dispatch, notifier);

        // The handler publishes, but the (simulated) commit then fails.
        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(new Ping(), () =>
        {
            dispatch.Publish("config:1", new RealtimeEvent("TaskActioned", new { a = 1 }));
            throw new InvalidOperationException("commit failed");
        }, default));

        Assert.Empty(notifier.Sent);
    }
}
