using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.RealTime;

/// <summary>
/// Pipeline behavior that flushes collected realtime events <b>after</b> the inner pipeline completes
/// successfully — which, for commands, includes the database commit (this behavior is registered outside the
/// modules' transaction behaviors). If the request throws (validation, a domain rule, or a failed commit),
/// nothing was drained, so no notification goes out — clients never see a change that rolled back.
/// Delivery is best-effort: a transport failure is logged, never surfaced to the caller, since the business
/// change has already committed.
/// </summary>
public sealed class RealtimeDispatchBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly RealtimeDispatch _dispatch;
    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<RealtimeDispatchBehavior<TRequest, TResponse>> _logger;

    public RealtimeDispatchBehavior(
        RealtimeDispatch dispatch,
        IRealtimeNotifier notifier,
        ILogger<RealtimeDispatchBehavior<TRequest, TResponse>> logger)
    {
        _dispatch = dispatch;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        // Reached only when the inner pipeline (including any transaction commit) succeeded.
        foreach (var (group, realtimeEvent) in _dispatch.Drain())
        {
            try
            {
                await _notifier.NotifyGroupAsync(group, realtimeEvent, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RealtimeLog.NotifyFailed(_logger, exception, realtimeEvent.Type, group);
            }
        }

        return response;
    }
}

internal static partial class RealtimeLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to publish realtime event {EventType} to group {Group}.")]
    public static partial void NotifyFailed(ILogger logger, Exception exception, string eventType, string group);
}
