namespace Common.RabbitMQ;

/// <summary>Exponential backoff, capped, shared by the consumer's redelivery delay and the durable
/// outbound relay's retry cadence so both compute a wait the same way.</summary>
internal static class RetryBackoff
{
    public static TimeSpan Compute(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Min(Math.Max(attempt, 1) - 1, 20); // cap the exponent, not just the result — avoids Math.Pow overflow
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, exponent));
        return delay < maxDelay ? delay : maxDelay;
    }
}
