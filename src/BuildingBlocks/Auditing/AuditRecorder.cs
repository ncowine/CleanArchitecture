using System.Diagnostics;
using BuildingBlocks.Correlation;

namespace BuildingBlocks.Auditing;

/// <summary>
/// Default <see cref="IAuditRecorder"/>: stamps a caller's <see cref="AuditFact"/> with the ambient
/// actor, correlation id, and timestamp, then hands it to the sink — the same sink the command pipeline
/// uses, so custom records need no separate destination, index, or dashboard.
/// </summary>
internal sealed class AuditRecorder : IAuditRecorder
{
    private readonly IAuditSink _sink;
    private readonly ICurrentActor _actor;
    private readonly ICorrelationContext _correlation;
    private readonly IAuditScope _scope;

    public AuditRecorder(IAuditSink sink, ICurrentActor actor, ICorrelationContext correlation, IAuditScope scope)
    {
        _sink = sink;
        _actor = actor;
        _correlation = correlation;
        _scope = scope;
    }

    public Task RecordAsync(AuditFact fact, CancellationToken cancellationToken = default)
        => _sink.RecordAsync(Entry(fact, DateTime.UtcNow), cancellationToken);

    public async Task<T> TrackAsync<T>(
        AuditFact fact, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var occurredOnUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            await _sink.RecordAsync(
                Entry(fact with { Succeeded = true, Error = null, ElapsedMs = stopwatch.ElapsedMilliseconds }, occurredOnUtc),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            // The failure is the thing most worth auditing, so record it even when the cancellation that
            // caused it would abort the sink write — hence CancellationToken.None here.
            await _sink.RecordAsync(
                Entry(fact with { Succeeded = false, Error = exception.Message, ElapsedMs = stopwatch.ElapsedMilliseconds }, occurredOnUtc),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task TrackAsync(
        AuditFact fact, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => TrackAsync<object?>(fact, async token =>
        {
            await operation(token).ConfigureAwait(false);
            return null;
        }, cancellationToken);

    public void Annotate(string key, string? value) => _scope.Annotate(key, value);

    private AuditEntry Entry(AuditFact fact, DateTime occurredOnUtc) => new(
        _correlation.CorrelationId,
        _actor.Current,
        fact.Action,
        occurredOnUtc,
        fact.Succeeded,
        fact.ElapsedMs,
        fact.Error,
        // Standalone records describe access, not a committed change-set; the entity changes of the
        // surrounding command belong to that command's own record, not duplicated onto this one.
        [],
        fact.Category,
        fact.Source,
        fact.Resource,
        // Same policy as an intercepted column value: secrets redacted, long values cut, count capped.
        AuditRedaction.Sanitize(fact.Details));
}
