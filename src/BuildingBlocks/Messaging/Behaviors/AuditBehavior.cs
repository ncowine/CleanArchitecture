using System.Diagnostics;
using BuildingBlocks.Auditing;
using BuildingBlocks.Correlation;

namespace BuildingBlocks.Messaging.Behaviors;

/// <summary>
/// Records an audit entry for every <see cref="IAuditableRequest"/> — who, what, when, outcome, and how
/// long it took — by wrapping the handler. Sits outside validation, so rejected commands are audited too.
/// Capture happens here once for all auditable requests; the destination is the swappable
/// <see cref="IAuditSink"/>. Requests marked <see cref="IAuditableRead"/> are recorded as reads, so
/// "who looked at this?" is captured by the same pipeline as "who changed this?".
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IAuditableRequest
{
    private readonly IAuditSink _sink;
    private readonly ICurrentActor _actor;
    private readonly ICorrelationContext _correlation;
    // The action names the operation, not the request class: see RequestName. Resolved once per closed
    // generic, since the request type is fixed.
    private static readonly string Action = RequestName.Feature(typeof(TRequest));

    private readonly IAuditScope _scope;

    public AuditBehavior(IAuditSink sink, ICurrentActor actor, ICorrelationContext correlation, IAuditScope scope)
    {
        _sink = sink;
        _actor = actor;
        _correlation = correlation;
        _scope = scope;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var actor = _actor.Current;
        var action = Action;
        var category = request is IAuditableRead ? AuditCategory.Read : AuditCategory.Write;
        var resource = request.AuditResource;
        var occurredOnUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();
            // The transaction behavior (nested inside this one) has committed by now, so the interceptor
            // has populated the scope with the committed before/after changes. Anything the handler
            // annotated on the way through (via IAuditRecorder.Annotate) rides along on the same record.
            await _sink.RecordAsync(
                new AuditEntry(
                    _correlation.CorrelationId, actor, action, occurredOnUtc, Succeeded: true,
                    stopwatch.ElapsedMilliseconds, Error: null, _scope.Changes.ToArray(),
                    category, Source: null, resource, Details(_scope)),
                cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            // Failed command: its transaction rolled back, so report no changes (nothing was committed).
            // The annotations still stand — they say how far the request got before it failed.
            await _sink.RecordAsync(
                new AuditEntry(
                    _correlation.CorrelationId, actor, action, occurredOnUtc, Succeeded: false,
                    stopwatch.ElapsedMilliseconds, exception.Message, [],
                    category, Source: null, resource, Details(_scope)),
                cancellationToken);
            throw;
        }
    }

    // A record with an empty details bag reads better in the store than one with an empty object.
    private static Dictionary<string, string?>? Details(IAuditScope scope)
        => scope.Details.Count == 0 ? null : new Dictionary<string, string?>(scope.Details, StringComparer.Ordinal);
}
