using System.Diagnostics;
using BuildingBlocks.Auditing;
using BuildingBlocks.Correlation;

namespace BuildingBlocks.Messaging.Behaviors;

/// <summary>
/// Records an audit entry for every <see cref="IAuditableRequest"/> — who, what, when, outcome, and how
/// long it took — by wrapping the handler. Sits outside validation, so rejected commands are audited too.
/// Capture happens here once for all auditable requests; the destination is the swappable
/// <see cref="IAuditSink"/>.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IAuditableRequest
{
    private readonly IAuditSink _sink;
    private readonly ICurrentActor _actor;
    private readonly ICorrelationContext _correlation;

    public AuditBehavior(IAuditSink sink, ICurrentActor actor, ICorrelationContext correlation)
    {
        _sink = sink;
        _actor = actor;
        _correlation = correlation;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = _actor.Current;
        // Vertical-slice commands are nested (e.g. CreateStudent.Command), so Type.Name is just
        // "Command" — use the enclosing feature type's name for a meaningful action.
        var requestType = typeof(TRequest);
        var action = requestType.DeclaringType?.Name ?? requestType.Name;
        var occurredOnUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();
            await _sink.RecordAsync(
                new AuditEntry(_correlation.CorrelationId, actor, action, occurredOnUtc, Succeeded: true, stopwatch.ElapsedMilliseconds, Error: null),
                cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            await _sink.RecordAsync(
                new AuditEntry(_correlation.CorrelationId, actor, action, occurredOnUtc, Succeeded: false, stopwatch.ElapsedMilliseconds, exception.Message),
                cancellationToken);
            throw;
        }
    }
}
