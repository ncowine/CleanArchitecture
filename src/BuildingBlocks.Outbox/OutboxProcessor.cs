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
/// at-least-once delivery (consumers must be idempotent), capped retries, then dead-letter.
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
            // Restore the originating correlation id so the consumer's work and logs trace back to the
            // request that produced this message.
            if (message.CorrelationId is not null)
            {
                correlation.Set(message.CorrelationId);
            }

            try
            {
                await dispatcher.DispatchAsync(message.Id, message.Type, message.Content, cancellationToken);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Error = exception.Message;

                if (message.Attempts >= MaxDeliveryAttempts)
                {
                    message.DeadLetteredOnUtc = DateTime.UtcNow;
                    OutboxLog.DeadLettered(_logger, exception, message.Id, message.Type, context, message.Attempts);
                }
                else
                {
                    OutboxLog.DeliveryFailed(_logger, exception, message.Id, message.Type, context, message.Attempts);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static partial class OutboxLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox processing tick failed for {Context}.")]
    public static partial void TickFailed(ILogger logger, Exception exception, string context);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deliver outbox message {MessageId} ({MessageType}) for {Context} on attempt {Attempts}; will retry.")]
    public static partial void DeliveryFailed(ILogger logger, Exception exception, Guid messageId, string messageType, string context, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering outbox message {MessageId} ({MessageType}) for {Context} after {Attempts} failed attempts.")]
    public static partial void DeadLettered(ILogger logger, Exception exception, Guid messageId, string messageType, string context, int attempts);
}
