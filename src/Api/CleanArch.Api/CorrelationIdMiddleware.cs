using BuildingBlocks.Correlation;

namespace CleanArch.Api;

/// <summary>
/// Establishes a correlation id for each request: reuses an inbound <c>X-Correlation-ID</c> header or
/// generates one, stores it in the scoped <see cref="ICorrelationContext"/>, echoes it on the response,
/// and opens a logging scope so every log line in the request carries it. Downstream, audit records and
/// outbox messages pick the same id up — so one id traces a request through to its async outbox delivery.
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICorrelationContext correlation,
        ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        correlation.Set(correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
