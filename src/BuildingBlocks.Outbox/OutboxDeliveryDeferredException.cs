namespace BuildingBlocks.Outbox;

/// <summary>
/// Thrown by an <see cref="IOutboxDispatcher{TContext}"/> when the message couldn't be delivered for a reason that
/// isn't the message's fault and will pass on its own — typically the message broker being unreachable. The
/// processor doesn't count it as a failed attempt, so an outage can't dead-letter good messages; the message (and
/// everything after it in the batch) waits for the next poll.
/// </summary>
public sealed class OutboxDeliveryDeferredException : Exception
{
    public OutboxDeliveryDeferredException(string message)
        : base(message)
    {
    }

    public OutboxDeliveryDeferredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OutboxDeliveryDeferredException()
    {
    }
}
