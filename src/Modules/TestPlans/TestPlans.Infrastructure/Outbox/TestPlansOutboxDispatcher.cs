using System.Text.Json;
using BuildingBlocks.Outbox;
using TesterGuide.Contracts;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Outbox;

/// <summary>
/// The Test Plans module's outbox dispatch logic (the sync saga's reverse leg): routes a
/// <see cref="MainDbActionRejected"/> to the Tester Guide module's published
/// <see cref="IGuideActionReconciler"/>, which flags the originating guide action as rejected. Plugged into
/// the shared <c>OutboxProcessor&lt;TestPlansDbContext&gt;</c>.
/// </summary>
internal sealed class TestPlansOutboxDispatcher : IOutboxDispatcher<TestPlansDbContext>
{
    private readonly IGuideActionReconciler _reconciler;

    public TestPlansOutboxDispatcher(IGuideActionReconciler reconciler)
    {
        _reconciler = reconciler;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(MainDbActionRejected):
                var rejected = JsonSerializer.Deserialize<MainDbActionRejected>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                return _reconciler.MarkSyncRejectedAsync(
                    messageId, rejected.SourceReference, rejected.Reason, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
