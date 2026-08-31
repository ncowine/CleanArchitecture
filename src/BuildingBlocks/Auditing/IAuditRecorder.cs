namespace BuildingBlocks.Auditing;

/// <summary>
/// The general-purpose capture point for audit: anything, from anywhere, not just database writes.
/// The pipeline behavior audits commands and the EF interceptor captures their before/after changes, but
/// plenty of things worth auditing are neither — a third-party API lookup, a file read, a cache hit, a
/// permission check. Inject this and record them explicitly; the ambient fields (actor, correlation id,
/// timestamp) are filled in for you, and the record lands in the same <see cref="IAuditSink"/> — so a
/// vendor API call and a database write show up side by side in the same Kibana view.
/// </summary>
/// <remarks>
/// Scoped, because it reads the current request's actor and correlation id. A singleton client that
/// needs to audit should either be registered scoped, or take <c>IServiceScopeFactory</c> and resolve a
/// recorder per call — resolving one at construction would capture the first request's identity forever.
/// </remarks>
public interface IAuditRecorder
{
    /// <summary>
    /// Writes one standalone audit record. Use when you already know the outcome (or there's nothing to
    /// time) — otherwise prefer <see cref="TrackAsync{T}"/>, which fills in success, error, and duration.
    /// </summary>
    Task RecordAsync(AuditFact fact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/>, times it, and records exactly one audit entry either way:
    /// succeeded with the elapsed time, or failed with the exception message. The exception is rethrown
    /// untouched — auditing never changes behavior.
    /// </summary>
    Task<T> TrackAsync<T>(
        AuditFact fact, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="TrackAsync{T}"/>
    Task TrackAsync(
        AuditFact fact, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a custom fact to the audit record of the request currently in flight, instead of writing
    /// a separate one. Use for context that belongs to the command or query being handled — "served from
    /// cache", "matched 42 rows", "vendor request id" — and it rides along on that single record.
    /// No-ops harmlessly when nothing is being audited (the annotation is simply never read).
    /// </summary>
    void Annotate(string key, string? value);
}
