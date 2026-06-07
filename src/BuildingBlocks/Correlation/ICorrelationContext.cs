namespace BuildingBlocks.Correlation;

/// <summary>
/// Carries the correlation id for the current logical operation so it can be attached to logs, audit
/// records, and outbox messages. Scoped: set once per request (by middleware) or per outbox message
/// (by the dispatcher), then read everywhere downstream — which is what lets a single id trace a flow
/// across the async outbox/saga hops.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }

    void Set(string correlationId);
}
