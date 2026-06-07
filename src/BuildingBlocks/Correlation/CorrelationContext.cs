namespace BuildingBlocks.Correlation;

internal sealed class CorrelationContext : ICorrelationContext
{
    // Defaults to a fresh id so background work (with no inbound request) is still correlated.
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString();

    public void Set(string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationId = correlationId;
        }
    }
}
