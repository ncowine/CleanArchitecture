using System.Diagnostics;
using BuildingBlocks.Correlation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Generic outbox dispatcher for a single <typeparamref name="TContext"/>. On a timer it pulls
/// undelivered, not-dead-lettered messages and hands each to the module's
/// <see cref="IOutboxDispatcher{TContext}"/>. Reliability properties (identical for every module):
/// at-least-once delivery (consumers must be idempotent), capped retries, then dead-letter. A dispatcher
/// that throws <see cref="OutboxDeliveryDeferredException"/> (e.g. the broker is down) doesn't use up an
/// attempt: the batch stops there and the message is tried again on the next poll.
/// </summary>
internal sealed class OutboxProcessor<TContext> : BackgroundService where TContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 20;
    private const int MaxDeliveryAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor<TContext>> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                OutboxLog.TickFailed(_logger, exception, typeof(TContext).Name);
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher<TContext>>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

        var messages = await db.Set<OutboxMessage>()
            .Where(message => message.ProcessedOnUtc == null && message.DeadLetteredOnUtc == null)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        var context = typeof(TContext).Name;

        foreach (var message in messages)
        {
            message.Attempts++;
            using var activity = StartDeliveryActivity(message, context);
            // Restore the originating correlation id so the consumer's work and logs trace back to the
            // request that produced this message. The log scope puts it on this delivery's log lines, as the
            // request's own lines have it, so looking the request up finds them too.
            using var logScope = message.CorrelationId is null
                ? null
                : _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = message.CorrelationId });
            if (message.CorrelationId is not null)
            {
                correlation.Set(message.CorrelationId);
            }

            try
            {
                await dispatcher.DispatchAsync(message.Id, message.Type, message.Content, cancellationToken);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;
                OutboxDiagnostics.Delivered.Add(1, new KeyValuePair<string, object?>("db", context));
                OutboxLog.Delivered(_logger, message.Id, message.Type, context, message.Attempts);
            }
            catch (OutboxDeliveryDeferredException exception)
            {
                // Not the message's fault (e.g. the broker is down): don't spend an attempt on it, and stop here so
                // the rest of the batch keeps its order and isn't tried against the same outage.
                message.Attempts--;
                message.Error = exception.Message;
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                OutboxLog.DeliveryDeferred(_logger, exception, message.Id, message.Type, context);
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Error = exception.Message;
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

                if (message.Attempts >= MaxDeliveryAttempts)
                {
                    message.DeadLetteredOnUtc = DateTime.UtcNow;
                    OutboxLog.DeadLettered(_logger, exception, message.Id, message.Type, context, message.Attempts);
                    OutboxDiagnostics.DeadLettered.Add(1, new KeyValuePair<string, object?>("db", context));
                }
                else
                {
                    OutboxLog.DeliveryFailed(_logger, exception, message.Id, message.Type, context, message.Attempts);
                    OutboxDiagnostics.Failed.Add(1, new KeyValuePair<string, object?>("db", context));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A span for one delivery attempt, continuing the trace of the request that wrote the row. The publish (and,
    /// through its traceparent header, the consumer) nests under it, so request -> publish -> consume is one trace.
    /// </summary>
    private static Activity? StartDeliveryActivity(OutboxMessage message, string context)
    {
        ActivityContext.TryParse(message.TraceParent, null, isRemote: true, out var parent);

        var activity = OutboxDiagnostics.ActivitySource.StartActivity($"outbox deliver {message.Type}", ActivityKind.Internal, parent);
        activity?.SetTag("outbox.db", context);
        activity?.SetTag("outbox.message.id", message.Id);
        activity?.SetTag("outbox.message.type", message.Type);
        activity?.SetTag("outbox.attempt", message.Attempts);
        return activity;
    }
}

internal static partial class OutboxLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox processing tick failed for {Context}.")]
    public static partial void TickFailed(ILogger logger, Exception exception, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered outbox message {MessageId} ({MessageType}) for {Context} on attempt {Attempts}.")]
    public static partial void Delivered(ILogger logger, Guid messageId, string messageType, string context, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deliver outbox message {MessageId} ({MessageType}) for {Context} on attempt {Attempts}; will retry.")]
    public static partial void DeliveryFailed(ILogger logger, Exception exception, Guid messageId, string messageType, string context, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deferred outbox message {MessageId} ({MessageType}) for {Context}; it will be retried on the next poll without using up an attempt.")]
    public static partial void DeliveryDeferred(ILogger logger, Exception exception, Guid messageId, string messageType, string context);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering outbox message {MessageId} ({MessageType}) for {Context} after {Attempts} failed attempts.")]
    public static partial void DeadLettered(ILogger logger, Exception exception, Guid messageId, string messageType, string context, int attempts);
}
