using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Behaviors;

/// <summary>
/// Logs the start and completion of every request. A worked example of a pipeline behavior —
/// copy this shape for validation, transactions, caching, etc.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        LoggingBehaviorLog.Handling(_logger, requestName);
        var response = await next();
        LoggingBehaviorLog.Handled(_logger, requestName);

        return response;
    }
}

// Compile-time logging via the source generator — zero allocations when the level is disabled.
internal static partial class LoggingBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
    public static partial void Handling(ILogger logger, string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request}")]
    public static partial void Handled(ILogger logger, string request);
}
