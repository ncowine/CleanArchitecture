using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Behaviors;

/// <summary>
/// Logs the start and completion of every request. A worked example of a pipeline behavior —
/// copy this shape for validation, transactions, caching, etc.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Resolved once per closed generic rather than per request: the request type never changes, and
    // reflecting on every call to produce a constant string is work for nothing.
    private static readonly string Request = RequestName.Display(typeof(TRequest));

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
        LoggingBehaviorLog.Handling(_logger, Request);

        var startedAt = Stopwatch.GetTimestamp();
        var response = await next();

        // Hoisted out of the log call: a timestamp delta is trivial to compute, and doing it here keeps
        // the analyzer honest about arguments that would be expensive when the level is disabled.
        var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LoggingBehaviorLog.Handled(_logger, Request, elapsedMs);

        return response;
    }
}

// Compile-time logging via the source generator — zero allocations when the level is disabled.
internal static partial class LoggingBehaviorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Request}")]
    public static partial void Handling(ILogger logger, string request);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request} in {ElapsedMs}ms")]
    public static partial void Handled(ILogger logger, string request, long elapsedMs);
}
